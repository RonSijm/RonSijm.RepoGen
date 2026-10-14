using System.Reflection;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.SqlServer;
using Xunit;

#pragma warning disable EF1002 // Identifiers are exclusively a hard-coded prefix plus a GUID for these test-owned tables.

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SqlServerAtomicWriteTests
{
    private static readonly string[] Contenders = ["first", "second"];

    [SqlServerFixtureFact]
    public async Task SameCompositeKeyInsertsHaveOneWinnerAndConditionalRowversionUpsertsHaveOneWinner()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var modelContext = fixture.Context();
        var assembly = ClosedRepositoryTests.Compile(Model(modelContext));
        var request = new SqlAtomicRequest(1, "special'; DROP TABLE anything; --", "untouched", []);
        var starts = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await starts.Task;
            await using var context = fixture.Context();
            var outcome = await Invoke(assembly, context, "EnsureAsync", request);
            context.ChangeTracker.Entries().Should().BeEmpty();
            return outcome;
        })).ToArray();
        starts.SetResult();
        var results = await Task.WhenAll(attempts);
        results.Count(result => result.Status == CommandStatus.Inserted).Should().Be(1);
        results.Count(result => result.Status == CommandStatus.AlreadyExists).Should().Be(7);
        var inserted = results.Single(result => result.HasValue).Value!;
        inserted.Id.Should().BeGreaterThan(0);
        inserted.Version.Should().HaveCount(8);
        inserted.Normalized.Should().Be(request.Code.ToUpperInvariant());
        inserted.Flag.Should().Be(SqlAtomicState.Pending);
        var updates = await Task.WhenAll(Contenders.Select(text => Task.Run(async () =>
        {
            await using var context = fixture.Context();
            return await Invoke(assembly, context, "UpsertAsync", request with { Text = text, Version = inserted.Version.ToArray() });
        })));
        updates.Count(result => result.Status == CommandStatus.Updated).Should().Be(1);
        updates.Count(result => result.Status == CommandStatus.NotMatched).Should().Be(1);
        var updated = updates.Single(result => result.HasValue).Value!;
        updated.Id.Should().Be(inserted.Id);
        updated.Version.Should().NotEqual(inserted.Version);
        (await modelContext.Rows.AsNoTracking().SingleAsync()).Version.Should().Equal(updated.Version);
        (await Invoke(assembly, modelContext, "EnsureAsync", request with { OwnerId = 2 })).Status.Should().Be(CommandStatus.Inserted);
        (await modelContext.Rows.CountAsync()).Should().Be(2);
    }

    [SqlServerFixtureFact]
    public async Task TrackedOriginalVersionsProtectPartialPatchesAndConsumeCannotReturnTwice()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Context();
        var assembly = ClosedRepositoryTests.Compile(Model(context));
        var request = new SqlAtomicRequest(1, "patch", "keep", []);
        var inserted = (await Invoke(assembly, context, "EnsureAsync", request)).Value!;
        await using var firstClient = fixture.Context();
        await using var secondClient = fixture.Context();
        await firstClient.Rows.SingleAsync();
        await secondClient.Rows.SingleAsync(); // Both hold the original version before either write.
        var clients = new[] { firstClient, secondClient };
        var races = await Task.WhenAll(Contenders.Select((text, index) => Task.Run(async () =>
        {
            return await Invoke(assembly, clients[index], "PatchAsync", request with { Text = text, Version = inserted.Version.ToArray() });
        })));
        races.Count(result => result.Status == CommandStatus.Applied).Should().Be(1);
        races.Count(result => result.Status == CommandStatus.Conflict).Should().Be(1);
        clients.Should().OnlyContain(client => !client.ChangeTracker.HasChanges());
        var winner = races.Single(result => result.HasValue).Value!;
        winner.Code.Should().Be("patch");
        winner.Version.Should().NotEqual(inserted.Version);
        var nullPatch = await Invoke(assembly, context, "PatchAsync", request with { Text = null, Version = winner.Version.ToArray() });
        nullPatch.Status.Should().Be(CommandStatus.Applied);
        nullPatch.Value!.Text.Should().BeNull();
        var consumes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await using var competing = fixture.Context();
            return await Invoke(assembly, competing, "ConsumeAsync", request with { Version = nullPatch.Value.Version.ToArray() });
        })));
        consumes.Count(result => result.Status == CommandStatus.Applied).Should().Be(1);
        consumes.Count(result => result.HasValue).Should().Be(1);
        consumes.Where(result => !result.Succeeded).Should().OnlyContain(result => result.Status == CommandStatus.Conflict || result.Status == CommandStatus.NotFound);
        (await context.Rows.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [SqlServerFixtureFact]
    public async Task ActiveWorkTransactionsDeferObserversRollbackWritesAndDoNotSwallowOtherConstraintErrors()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Context();
        var assembly = ClosedRepositoryTests.Compile(Model(context));
        var observer = new Recorder();
        await using (var work = new RepositoryUnitOfWork<SqlAtomicContext>(context, [observer]))
        await using (var transaction = await work.BeginTransactionAsync())
        {
            (await Invoke(assembly, work, "EnsureAsync", new SqlAtomicRequest(1, "rollback", null, []))).Status.Should().Be(CommandStatus.Inserted);
            observer.Calls.Should().Be(0);
            await transaction.RollbackAsync();
        }

        (await context.Rows.CountAsync()).Should().Be(0);
        observer.Calls.Should().Be(0);
        await using (var work = new RepositoryUnitOfWork<SqlAtomicContext>(context, [observer]))
        await using (var transaction = await work.BeginTransactionAsync())
        {
            (await Invoke(assembly, work, "EnsureAsync", new SqlAtomicRequest(1, "commit", null, []))).Status.Should().Be(CommandStatus.Inserted);
            observer.Calls.Should().Be(0);
            await transaction.CommitAsync();
        }

        observer.Calls.Should().Be(1);
        Func<Task> invalid = async () => await Invoke(assembly, context, "EnsureAsync", new SqlAtomicRequest(-1, "bad", null, []));
        (await invalid.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(547);
        context.Database.CurrentTransaction.Should().BeNull();
        (await context.Rows.CountAsync()).Should().Be(1);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Func<Task> cancel = async () => await Invoke(assembly, context, "EnsureAsync", new SqlAtomicRequest(1, "canceled", null, []), canceled.Token);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        (await context.Rows.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("trigger")]
    [InlineData("shadow")]
    [InlineData("shared")]
    public void UnsupportedModelsAreRejectedWithoutADatabaseConnection(string shape)
    {
        using var context = new SqlAtomicContext("Server=invalid;Database=invalid", "unused", shape);
        Action validate = () => SqlServerAtomicWriter.ValidateModel(context.Model.FindEntityType(typeof(SqlAtomicRow))!);
        validate.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void SqlServerWriterCannotBeConfiguredAgainstSqlite()
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Atomic.Contracts").UseAtomicWrites<SqlServerAtomicWriter>());
        Action generate = () => RepositoryModelFactory.Create(context, typeof(CommandContext).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*Atomic writer requires*SqlServer*Sqlite*");
    }

    private static RepositoryGenerationModel Model(SqlAtomicContext context)
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("SqlAtomic.Contracts").UseAsyncContextLeases()
            .UseAtomicWrites<SqlServerAtomicWriter>().GenerateOnlyConfiguredEntities());
        builder.Entity<SqlAtomicRow>(entity =>
        {
            entity.HasCommand("Ensure").Create<SqlAtomicRequest>(request => new SqlAtomicRow { OwnerId = request.OwnerId, Code = request.Code, Text = request.Text })
                .AllowWrite(row => row.OwnerId).InsertIfAbsent(row => new { row.OwnerId, row.Code })
                .Returns(row => new SqlAtomicDto(row.Id, row.OwnerId, row.Code, row.Text, row.Normalized, row.Version, row.Flag));
            entity.HasCommand("Upsert").Create<SqlAtomicRequest>(request => new SqlAtomicRow { OwnerId = request.OwnerId, Code = request.Code, Text = request.Text })
                .AllowWrite(row => row.OwnerId).InsertOrUpdate(row => new { row.OwnerId, row.Code }, setters =>
                    setters.SetProperty<string?, SqlAtomicRequest>(row => row.Text, (row, request) => request.Text))
                .ExpectVersion<byte[], SqlAtomicRequest>(row => row.Version, (row, request) => request.Version)
                .Returns(row => new SqlAtomicDto(row.Id, row.OwnerId, row.Code, row.Text, row.Normalized, row.Version, row.Flag));
            entity.HasCommand("Patch").Where<SqlAtomicRequest>((row, request) => row.OwnerId == request.OwnerId && row.Code == request.Code)
                .Patch(setters => setters.SetProperty<string?, SqlAtomicRequest>(row => row.Text, (row, request) => request.Text))
                .ExpectVersion<byte[], SqlAtomicRequest>(row => row.Version, (row, request) => request.Version).Save()
                .Returns(row => new SqlAtomicDto(row.Id, row.OwnerId, row.Code, row.Text, row.Normalized, row.Version, row.Flag));
            entity.HasCommand("Consume").Where<SqlAtomicRequest>((row, request) => row.OwnerId == request.OwnerId && row.Code == request.Code)
                .TrackedDelete().ExpectVersion<byte[], SqlAtomicRequest>(row => row.Version, (row, request) => request.Version).Save()
                .Returns(row => new SqlAtomicDto(row.Id, row.OwnerId, row.Code, row.Text, row.Normalized, row.Version, row.Flag));
        });
        return RepositoryModelFactory.Create(context, typeof(SqlAtomicContext).Assembly, true, builder.Build());
    }

    private static Task<CommandResult<SqlAtomicDto>> Invoke(Assembly assembly, SqlAtomicContext context, string method, SqlAtomicRequest request, CancellationToken token = default)
        => Invoke(assembly, new ScopedDbContextLeaseProvider<SqlAtomicContext>(context), method, request, token);

    private static Task<CommandResult<SqlAtomicDto>> Invoke(Assembly assembly, IDbContextLeaseProvider<SqlAtomicContext> source, string method, SqlAtomicRequest request, CancellationToken token = default)
    {
        var instance = Activator.CreateInstance(assembly.GetType(typeof(SqlAtomicRow).Namespace + ".SqlAtomicRowRepositoryOperations")!, source, new SqlServerAtomicWriter())!;
        return (Task<CommandResult<SqlAtomicDto>>)instance.GetType().GetMethod(method)!.Invoke(instance, [request, token])!;
    }

    private sealed class Recorder : IRepositoryCommitObserver
    {
        public int Calls { get; private set; }

        public Task CommittedAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture(string connection, string table) : IAsyncDisposable
    {
        private string Table { get; } = table;
        public SqlAtomicContext Context() => new(connection, Table);

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture(Environment.GetEnvironmentVariable("REPOGEN_SQLSERVER_TEST_CONNECTION")!, "RepoGenAtomic_" + Guid.NewGuid().ToString("N"));
            await using var context = fixture.Context();
            await context.Database.ExecuteSqlRawAsync($"""
                                                       IF OBJECT_ID(N'dbo.{fixture.Table}', N'U') IS NOT NULL THROW 51000, 'Fixture table collision', 1;
                                                       CREATE TABLE [dbo].[{fixture.Table}] (
                                                           [Id] int IDENTITY NOT NULL PRIMARY KEY, [OwnerId] int NOT NULL CHECK ([OwnerId] > 0),
                                                           [business]]code] nvarchar(128) NOT NULL, [Text] nvarchar(max) NULL,
                                                           [Normalized] AS UPPER([business]]code]), [Version] rowversion NOT NULL,
                                                           [Flag] nvarchar(10) NOT NULL DEFAULT N'Pending', UNIQUE ([OwnerId], [business]]code])
                                                       );
                                                       """);
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = Context();
            await context.Database.ExecuteSqlRawAsync($"DROP TABLE [dbo].[{Table}];");
        }
    }
}

public sealed record SqlAtomicRequest(int OwnerId, string Code, string? Text, byte[] Version);

public sealed record SqlAtomicDto(int Id, int OwnerId, string Code, string? Text, string Normalized, byte[] Version, SqlAtomicState Flag);

public enum SqlAtomicState
{
    Pending,
    Ready
}

public sealed class SqlAtomicRow
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Code { get; set; } = "";
    public string? Text { get; set; }
    public string Normalized { get; set; } = "";
    public byte[] Version { get; set; } = [];
    public SqlAtomicState Flag { get; set; }
}

public sealed class SqlAtomicExtra
{
    public int Id { get; set; }
}

public sealed class SqlAtomicContext(string connection, string table, string? shape = null) : DbContext
{
    public string Table { get; } = table;
    public string? Shape { get; } = shape;
    public DbSet<SqlAtomicRow> Rows => Set<SqlAtomicRow>();
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseSqlServer(connection).ReplaceService<IModelCacheKeyFactory, SqlAtomicModelKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SqlAtomicRow>();
        entity.ToTable(Table, "dbo", tableBuilder =>
        {
            if (Shape == "trigger")
            {
                tableBuilder.HasTrigger("FixtureTrigger");
            }
        });
        entity.HasIndex(row => new { row.OwnerId, row.Code }).IsUnique();
        entity.Property(row => row.Code).HasMaxLength(128).HasColumnName("business]code");
        entity.Property(row => row.Normalized).HasComputedColumnSql("UPPER([business]]code])");
        entity.Property(row => row.Version).IsRowVersion();
        entity.Property(row => row.Flag).HasConversion<string>().HasMaxLength(10).HasDefaultValue(SqlAtomicState.Pending);
        if (Shape == "shadow")
        {
            entity.Property<string>("Shadow");
        }

        if (Shape == "shared")
        {
            modelBuilder.Entity<SqlAtomicExtra>().ToTable(Table, "dbo");
            entity.HasOne<SqlAtomicExtra>().WithOne().HasForeignKey<SqlAtomicExtra>(row => row.Id);
        }
    }
}

public sealed class SqlAtomicModelKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), ((SqlAtomicContext)context).Table, ((SqlAtomicContext)context).Shape, designTime);
}