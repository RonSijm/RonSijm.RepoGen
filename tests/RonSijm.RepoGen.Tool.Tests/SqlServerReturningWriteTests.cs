using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.SqlServer;

#pragma warning disable EF1002 // Only a hard-coded prefix plus a GUID identifies the fixture-owned tempdb table.

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SqlServerReturningWriteTests
{
    [SqlServerFixtureFact]
    public async Task NativeBulkOutputReturnsComputedValuesConvertersAndBinaryVersionsWithCompositeKeys()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Context();
        await Seed(context);
        var assembly = ClosedRepositoryTests.Compile(Model(context), rejectWarnings: true);
        var operations = Operations(assembly, new ScopedDbContextLeaseProvider<SqlReturningContext>(context));
        var request = new SqlReturningRequest("", "'; DROP TABLE anything; --", 3, []);
        var changed = await Call(operations, "IncreaseAsync", request);
        changed.AffectedRows.Should().Be(2);
        changed.Items.Should().OnlyContain(row => row.Quantity == 4 && row.Text == request.Text && row.State == ReturningState.Ready && row.Version.Length == 8);
        foreach (var row in changed.Items)
        {
            row.Normalized.Should().Be(row.Code.ToUpperInvariant());
        }

        (await context.Rows.CountAsync(row => row.State == ReturningState.Ready)).Should().Be(2);
        context.ChangeTracker.Entries().Should().BeEmpty();
        (await context.Rows.IgnoreQueryFilters().CountAsync(row => row.Quantity == 1)).Should().Be(2);
        var removed = await Call(operations, "RemoveAsync", request);
        removed.AffectedRows.Should().Be(2);
        removed.Items.Should().BeEquivalentTo(changed.Items);
        (await context.Rows.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await Call(operations, "RemoveAsync", request)).Items.Should().BeEmpty();
    }

    [SqlServerFixtureFact]
    public async Task NativeExpectedRowversionUpdatesAndConsumesCannotReturnTheSameVersionTwice()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Context();
        await Seed(context);
        var assembly = ClosedRepositoryTests.Compile(Model(context));
        var original = await context.Rows.AsNoTracking().SingleAsync(row => row.OwnerId == 1 && row.Code == "item1");
        var updates = await Race("CompareAndSetAsync", original.Version);
        updates.Count(result => result.AffectedRows == 1).Should().Be(1);
        updates.Count(result => result.AffectedRows == 0).Should().Be(3);
        var winner = updates.Single(result => result.AffectedRows == 1).Items[0];
        winner.Version.Should().NotEqual(original.Version);
        var deletes = await Race("ConsumeAsync", winner.Version);
        deletes.Count(result => result.AffectedRows == 1).Should().Be(1);
        deletes.Count(result => result.AffectedRows == 0).Should().Be(3);
        deletes.Single(result => result.AffectedRows == 1).Items[0].Version.Should().Equal(winner.Version);
        (await context.Rows.CountAsync(row => row.OwnerId == 1 && row.Code == "item1")).Should().Be(0);

        Task<MutationResult<SqlReturningDto>[]> Race(string method, byte[] version) => Task.WhenAll(Enumerable.Range(0, 4).Select(index => Task.Run(async () =>
        {
            await using var competitor = fixture.Context();
            var operations = Operations(assembly, new ScopedDbContextLeaseProvider<SqlReturningContext>(competitor));
            var result = await Call(operations, method, new SqlReturningRequest("item1", "winner" + index, 1, version));
            competitor.ChangeTracker.Entries().Should().BeEmpty();
            return result;
        })));
    }

    [SqlServerFixtureFact]
    public async Task SqlServerOutputMappingFailuresUseSavepointsAndCommitObserversWaitForTheRealCommit()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Context();
        await Seed(context);
        var recorder = new Recorder();
        var assembly = ClosedRepositoryTests.Compile(Model(context));
        await using var work = new RepositoryUnitOfWork<SqlReturningContext>(context, [recorder]);
        var operations = Operations(assembly, work);
        await using var transaction = await work.BeginTransactionAsync();
        await Call(operations, "IncreaseAsync", new SqlReturningRequest("", null, 2, []));
        Func<Task> failed = async () => await ReturningWriteTests.Call<Task<MutationResult<int>>>(operations, "FailAsync", CancellationToken.None);
        await failed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*returning projection failure*");
        (await context.Rows.AsNoTracking().Where(row => row.OwnerId == 1).ToListAsync()).Should().OnlyContain(row => row.Quantity == 3 && row.Text == null);
        recorder.Calls.Should().Be(0);
        await transaction.CommitAsync();
        recorder.Calls.Should().Be(1);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [SqlServerFixtureFact]
    public async Task SqlServerStandaloneMappingFailureAndSharedRollbackUndoNativeDml()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Context();
        await Seed(context);
        var assembly = ClosedRepositoryTests.Compile(Model(context));
        var recorder = new Recorder();
        await using (var work = new RepositoryUnitOfWork<SqlReturningContext>(context, [recorder]))
        {
            var operations = Operations(assembly, work);
            Func<Task> failed = async () => await ReturningWriteTests.Call<Task<MutationResult<int>>>(operations, "FailAsync", CancellationToken.None);
            await failed.Should().ThrowAsync<InvalidOperationException>();
            recorder.Calls.Should().Be(0);
            await using var transaction = await work.BeginTransactionAsync();
            (await Call(operations, "RemoveAsync", new SqlReturningRequest("", null, 0, []))).AffectedRows.Should().Be(2);
            await transaction.RollbackAsync();
            recorder.Calls.Should().Be(0);
        }

        (await context.Rows.IgnoreQueryFilters().ToListAsync()).Should().HaveCount(4).And.OnlyContain(row => row.Quantity == 1);
    }

    [SqlServerFixtureFact]
    public async Task AnExistingMarsTransactionIsRejectedBeforeWritingBecauseItHasNoSafeSavepoint()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Context(mars: true);
        await Seed(context);
        var assembly = ClosedRepositoryTests.Compile(Model(context));
        await using var transaction = await context.Database.BeginTransactionAsync();
        transaction.SupportsSavepoints.Should().BeFalse();
        Func<Task> unsupported = async () => await Call(Operations(assembly, new ScopedDbContextLeaseProvider<SqlReturningContext>(context)), "IncreaseAsync", new SqlReturningRequest("", null, 2, []));
        await unsupported.Should().ThrowAsync<NotSupportedException>().WithMessage("*savepoint*");
        (await context.Rows.IgnoreQueryFilters().ToListAsync()).Should().OnlyContain(row => row.Quantity == 1);
    }

    private static RepositoryGenerationModel Model(SqlReturningContext context)
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("SqlReturning.Contracts")
            .UseAsyncContextLeases().UseReturningWrites<SqlServerReturningWriter>().GenerateOnlyConfiguredEntities());
        builder.Entity<SqlReturningRow>(entity =>
        {
            entity.HasVisibilityPolicy("owner", row => row.OwnerId == 1);
            entity.HasCommand("Increase").Update(setters => setters.SetProperty<int, SqlReturningRequest>(row => row.Quantity, (row, request) => row.Quantity + request.Increment)
                    .SetProperty<string?, SqlReturningRequest>(row => row.Text, (row, request) => request.Text).SetProperty(row => row.State, ReturningState.Ready))
                .ReturnsMany(row => new SqlReturningDto(row.OwnerId, row.Code, row.Quantity, row.Text, row.Normalized, row.State, row.Version));
            entity.HasCommand("Remove").Where<SqlReturningRequest>((row, request) => request.Code == "" || row.Code == request.Code)
                .Delete().ReturnsMany(row => new SqlReturningDto(row.OwnerId, row.Code, row.Quantity, row.Text, row.Normalized, row.State, row.Version));
            entity.HasCommand("CompareAndSet").Where<SqlReturningRequest>((row, request) => row.Code == request.Code)
                .ExpectVersion<byte[], SqlReturningRequest>(row => row.Version, (row, request) => request.Version)
                .Update(setters => setters.SetProperty<string?, SqlReturningRequest>(row => row.Text, (row, request) => request.Text))
                .ReturnsMany(row => new SqlReturningDto(row.OwnerId, row.Code, row.Quantity, row.Text, row.Normalized, row.State, row.Version));
            entity.HasCommand("Consume").Where<SqlReturningRequest>((row, request) => row.Code == request.Code)
                .ExpectVersion<byte[], SqlReturningRequest>(row => row.Version, (row, request) => request.Version).Delete()
                .ReturnsMany(row => new SqlReturningDto(row.OwnerId, row.Code, row.Quantity, row.Text, row.Normalized, row.State, row.Version));
            entity.HasCommand("Fail").Update(setters => setters.SetProperty(row => row.Quantity, row => row.Quantity + 1)).ReturnsMany(row => ReturningMapping.Fail(row.Quantity));
        });
        return RepositoryModelFactory.Create(context, typeof(SqlReturningRow).Assembly, true, builder.Build());
    }

    private static async Task Seed(SqlReturningContext context)
    {
        context.Rows.AddRange(new SqlReturningRow { OwnerId = 1, Code = "item1" }, new SqlReturningRow { OwnerId = 1, Code = "item2" },
            new SqlReturningRow { OwnerId = 1, Code = "hidden", Archived = true }, new SqlReturningRow { OwnerId = 2, Code = "item1" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static object Operations(Assembly assembly, IDbContextLeaseProvider<SqlReturningContext> source) => Activator.CreateInstance(
        assembly.GetType(typeof(SqlReturningRow).Namespace + ".SqlReturningRowRepositoryOperations")!, source, new SqlServerReturningWriter())!;

    private static Task<MutationResult<SqlReturningDto>> Call(object operations, string method, SqlReturningRequest request) =>
        ReturningWriteTests.Call<Task<MutationResult<SqlReturningDto>>>(operations, method, request, CancellationToken.None);

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
        public SqlReturningContext Context(bool mars = false)
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection) { MultipleActiveResultSets = mars };
            return new(builder.ConnectionString, Table);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture(Environment.GetEnvironmentVariable("REPOGEN_SQLSERVER_TEST_CONNECTION")!, "RepoGenReturning_" + Guid.NewGuid().ToString("N"));
            await using var context = fixture.Context();
            await context.Database.ExecuteSqlRawAsync($"""
                                                       IF OBJECT_ID(N'dbo.{fixture.Table}', N'U') IS NOT NULL THROW 51000, 'Fixture table collision', 1;
                                                       CREATE TABLE [dbo].[{fixture.Table}] (
                                                           [OwnerId] int NOT NULL, [business]]code] nvarchar(128) NOT NULL,
                                                           [Quantity] int NOT NULL CHECK ([Quantity] >= 0), [Text] nvarchar(max) NULL,
                                                           [Normalized] AS UPPER([business]]code]), [State] nvarchar(16) NOT NULL,
                                                           [Archived] bit NOT NULL, [Version] rowversion NOT NULL,
                                                           PRIMARY KEY ([OwnerId], [business]]code])
                                                       );
                                                       """);
            return fixture;
        }

        private string Table { get; } = table;

        public async ValueTask DisposeAsync()
        {
            await using var context = Context();
            await context.Database.ExecuteSqlRawAsync($"DROP TABLE [dbo].[{Table}];");
        }
    }
}

public sealed record SqlReturningRequest(string Code, string? Text, int Increment, byte[] Version);

public sealed record SqlReturningDto(int OwnerId, string Code, int Quantity, string? Text, string Normalized, ReturningState State, byte[] Version);

public sealed class SqlReturningRow
{
    public int OwnerId { get; set; }
    public string Code { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string? Text { get; set; }
    public string Normalized { get; set; } = "";
    public ReturningState State { get; set; }
    public bool Archived { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class SqlReturningContext(string connection, string table) : DbContext
{
    public string Table { get; } = table;
    public DbSet<SqlReturningRow> Rows => Set<SqlReturningRow>();
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseSqlServer(connection).ReplaceService<IModelCacheKeyFactory, SqlReturningModelKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SqlReturningRow>();
        entity.ToTable(Table, "dbo");
        entity.HasKey(row => new { row.OwnerId, row.Code });
        entity.Property(row => row.Code).HasColumnName("business]code").HasMaxLength(128);
        entity.Property(row => row.Normalized).HasComputedColumnSql("UPPER([business]]code])");
        entity.Property(row => row.State).HasConversion<string>().HasMaxLength(16);
        entity.Property(row => row.Version).IsRowVersion();
        entity.HasQueryFilter(row => !row.Archived);
    }
}

public sealed class SqlReturningModelKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), ((SqlReturningContext)context).Table, designTime);
}