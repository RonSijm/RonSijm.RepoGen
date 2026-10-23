using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class TrackedCommandTests
{
    [Fact]
    public async Task CreateReturnsGeneratedValuesAndBindsAnEfFreeWritePort()
    {
        await using var fixture = await Fixture.CreateAsync();
        var model = Model(fixture.Context);
        var port = (ITypedWrites)fixture.Operations(ClosedRepositoryTests.Compile(model), new ScopedDbContextLeaseProvider<CommandContext>(fixture.Context));
        var result = await port.CreateAsync(new CreateCommand("Ada", "original", 1));
        result.Status.Should().Be(CommandStatus.Applied);
        result.Value!.Id.Should().BeGreaterThan(2);
        result.Value.Normalized.Should().Be("ADA");
        result.Value.Version.Should().Be(1);
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(model);
        contracts.Should().Contain("ITypedWrites").And.NotContain("DbContext").And.NotContain("Selector");
        RepositoryManifestRenderer.Render(model).Should().Contain("\"persistence\": \"save\"").And.Contain("Revision");
    }

    [Fact]
    public async Task PatchPreservesOmittedFieldsAndOwnerScopeAndDistinguishesNullMissingAndConflict()
    {
        await using var fixture = await Fixture.CreateAsync();
        var port = (ITypedWrites)fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context)), new ScopedDbContextLeaseProvider<CommandContext>(fixture.Context));
        var result = await port.PatchAsync(new PatchCommand(1, 1, default, new Optional<string?>(null), 1, 2));
        result.Value!.Name.Should().Be("Initial");
        result.Value.Description.Should().BeNull();
        result.Value.Version.Should().Be(2);
        var missing = await port.PatchAsync(new PatchCommand(2, 1, new Optional<string>("Illegal"), default, 1, 2));
        missing.Status.Should().Be(CommandStatus.NotFound);
        var stale = await port.PatchAsync(new PatchCommand(1, 1, new Optional<string>("Stale"), default, 1, 3));
        stale.Status.Should().Be(CommandStatus.Conflict);
        stale.HasValue.Should().BeFalse();
        fixture.Context.ChangeTracker.Entries().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        (await fixture.Context.Rows.AsNoTracking().SingleAsync(row => row.Id == 2)).Name.Should().Be("Other owner");
        (await fixture.Context.Rows.AsNoTracking().SingleAsync(row => row.Id == 1)).Name.Should().Be("Initial");
    }

    [Fact]
    public async Task RaceDuringSaveReturnsConflictAndRestoresOnlyTheCommandsTrackerChanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        var race = new VersionRace();
        await using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(fixture.Connection).AddInterceptors(race).Options);
        var port = (ITypedWrites)fixture.Operations(ClosedRepositoryTests.Compile(Model(context)), new ScopedDbContextLeaseProvider<CommandContext>(context));
        var result = await port.PatchAsync(new PatchCommand(1, 1, new Optional<string>("Losing"), default, 1, 2));
        result.Status.Should().Be(CommandStatus.Conflict);
        context.ChangeTracker.Entries().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        var actual = await context.Rows.AsNoTracking().SingleAsync(row => row.Id == 1);
        actual.Name.Should().Be("Initial");
        actual.Revision.Should().Be(2);
    }

    [Fact]
    public async Task StageManySaveOnceAndExplicitTransactionRollbackPreserveCommitSemantics()
    {
        await using var fixture = await Fixture.CreateAsync();
        var observer = new CommitRecorder();
        await using var work = new RepositoryUnitOfWork<CommandContext>(fixture.Context, [observer]);
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context)), work);
        var request = new CreateCommand("Staged", "pending", 1);
        (await Invoke<Task<CommandResult>>(operations, "StageCreateAsync", request, CancellationToken.None)).Status.Should().Be(CommandStatus.Staged);
        (await Invoke<Task<CommandResult>>(operations, "StageCreateAsync", request with { Name = "Also staged" }, CancellationToken.None)).Status.Should().Be(CommandStatus.Staged);
        (await fixture.Context.Rows.AsNoTracking().CountAsync()).Should().Be(2);
        observer.Calls.Should().Be(0);
        (await work.SaveChangesAsync()).Should().Be(2);
        observer.Calls.Should().Be(1);
        await using var transaction = await work.BeginTransactionAsync();
        var result = await ((ITypedWrites)operations).CreateAsync(request with { Name = "Rolled back" });
        result.Succeeded.Should().BeTrue();
        observer.Calls.Should().Be(1);
        await transaction.RollbackAsync();
        (await fixture.Context.Rows.AsNoTracking().AnyAsync(row => row.Name == "Rolled back")).Should().BeFalse();
        observer.Calls.Should().Be(1);
    }

    [Fact]
    public async Task OwnedLeasesRejectStagingAndSavedWritesDisposeExactlyOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = new OwnedProvider(fixture.Connection);
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context)), provider);
        Func<Task> stage = async () => await Invoke<Task<CommandResult>>(operations, "StageCreateAsync", new CreateCommand("Lost", null, 1), CancellationToken.None);
        await stage.Should().ThrowAsync<InvalidOperationException>().WithMessage("*staged*borrowed*");
        (await ((ITypedWrites)operations).CreateAsync(new CreateCommand("Owned", null, 1))).Succeeded.Should().BeTrue();
        provider.Contexts.Should().HaveCount(2).And.OnlyContain(context => context.DisposeCount == 1);
        (await fixture.Context.Rows.AsNoTracking().AnyAsync(row => row.Name == "Lost")).Should().BeFalse();
    }

    [Fact]
    public async Task SetBasedExpectedVersionUsesNotMatchedWithoutGuessingAbsenceAndTrackedDeleteCanReturnData()
    {
        await using var fixture = await Fixture.CreateAsync();
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context)), new ScopedDbContextLeaseProvider<CommandContext>(fixture.Context));
        var request = new PatchCommand(1, 1, default, default, 1, 2);
        (await Invoke<Task<CommandResult>>(operations, "CompareAndSetAsync", request, CancellationToken.None)).Status.Should().Be(CommandStatus.Applied);
        (await Invoke<Task<CommandResult>>(operations, "CompareAndSetAsync", request, CancellationToken.None)).Status.Should().Be(CommandStatus.NotMatched);
        (await Invoke<Task<CommandResult>>(operations, "CompareAndSetAsync", request with { Id = 999 }, CancellationToken.None)).Status.Should().Be(CommandStatus.NotMatched);
        var deleted = await Invoke<Task<CommandResult<CommandDto>>>(operations, "DeleteSavedAsync", request with { ExpectedVersion = 2 }, CancellationToken.None);
        deleted.Value!.Name.Should().Be("Initial");
        (await fixture.Context.Rows.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SaveFailuresRestorePreExistingPendingValuesAndResultFailuresAreReportedAfterSave()
    {
        await using var fixture = await Fixture.CreateAsync();
        var port = (ITypedWrites)fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context)), new ScopedDbContextLeaseProvider<CommandContext>(fixture.Context));
        var row = await fixture.Context.Rows.SingleAsync(row => row.Id == 1);
        row.Description = "pre-existing pending edit";
        Func<Task> duplicate = async () => await port.PatchAsync(new PatchCommand(1, 1, new Optional<string>("Other owner"), default, 1, 2));
        await duplicate.Should().ThrowAsync<DbUpdateException>();
        row.Name.Should().Be("Initial");
        row.Description.Should().Be("pre-existing pending edit");
        row.Revision.Should().Be(1);
        fixture.Context.Entry(row).Property(item => item.Description).IsModified.Should().BeTrue();
        fixture.Context.Entry(row).Property(item => item.Name).IsModified.Should().BeFalse();
        fixture.Context.ChangeTracker.Clear();
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context)), new ScopedDbContextLeaseProvider<CommandContext>(fixture.Context));
        Func<Task> mapping = async () => await Invoke<Task<CommandResult<CommandDto>>>(operations, "FailingResultAsync", new CreateCommand("Saved before failure", null, 1), CancellationToken.None);
        await mapping.Should().ThrowAsync<RepositorySavedResultException>();
        (await fixture.Context.Rows.AsNoTracking().AnyAsync(row => row.Name == "Saved before failure")).Should().BeTrue();
    }

    [Fact]
    public async Task AFailedSavedResultStillInvalidatesTheLegacyDecorator()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().GenerateCacheDecorator().GenerateOnlyConfiguredEntities());
        builder.Entity<CommandRow>().HasCommand("Fail").Create<CreateCommand>(request => new CommandRow { Name = request.Name, OwnerId = request.OwnerId })
            .AllowWrite(row => row.OwnerId).Save().Returns(row => CommandMapping.Fail(row));
        var assembly = ClosedRepositoryTests.Compile(RepositoryModelFactory.Create(fixture.Context, typeof(CommandContext).Assembly, true, builder.Build()));
        var inner = Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandRowRepository")!, fixture.Context)!;
        var cache = DispatchProxy.Create(assembly.GetType("RonSijm.RepoGen.IRepositoryCacheProvider")!, typeof(MultiCacheProxy));
        var decorated = Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryCacheDecorator")!, inner, cache)!;
        Func<Task> fail = async () => await Invoke<Task<CommandResult<CommandDto>>>(decorated, "FailAsync", new CreateCommand("persisted", null, 1), CancellationToken.None);
        await fail.Should().ThrowAsync<RepositorySavedResultException>();
        ((MultiCacheProxy)cache).Invalidated.Should().Contain(typeof(CommandRow).FullName!);
        (await fixture.Context.Rows.AsNoTracking().AnyAsync(row => row.Name == "persisted")).Should().BeTrue();
    }

    [Fact]
    public void JsonPresenceDistinguishesMissingNullAndPresentValues()
    {
        JsonSerializer.Deserialize<OptionalPatch>("{}")!.Description.IsSpecified.Should().BeFalse();
        var explicitNull = JsonSerializer.Deserialize<OptionalPatch>("{\"Description\":null}")!;
        explicitNull.Description.IsSpecified.Should().BeTrue();
        explicitNull.Description.Value.Should().BeNull();
        JsonSerializer.Deserialize<OptionalPatch>("{\"Description\":\"text\"}")!.Description.Value.Should().Be("text");
    }

    [Theory]
    [InlineData("noPersistence")]
    [InlineData("stageResult")]
    [InlineData("generated")]
    [InlineData("ownership")]
    [InlineData("key")]
    [InlineData("notVersion")]
    [InlineData("fetchedVersion")]
    [InlineData("graph")]
    [InlineData("constructor")]
    public void InvalidMappingsFailAtGeneration(string scenario)
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.Commands").GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<CommandRow>();
        var command = entity.HasCommand("Bad");
        if (scenario == "noPersistence")
        {
            command.Create<CreateCommand>(request => new CommandRow { Name = request.Name });
        }

        if (scenario == "stageResult")
        {
            command.Create<CreateCommand>(request => new CommandRow { Name = request.Name }).Stage().Returns(row => row.Id);
        }

        if (scenario == "generated")
        {
            command.Create<CreateCommand>(request => new CommandRow { Normalized = request.Name }).Save();
        }

        if (scenario == "ownership")
        {
            command.Create<CreateCommand>(request => new CommandRow { OwnerId = request.OwnerId }).Save();
        }

        if (scenario == "graph")
        {
            command.Create<CreateCommand>(request => new CommandRow { Owner = new CommandOwner() }).Save();
        }

        if (scenario == "constructor")
        {
            command.Create<CreateCommand>(request => new CommandRow()).Save();
        }

        if (scenario is "key" or "notVersion" or "fetchedVersion")
        {
            command.Patch(update => update.SetProperty(row => row.Name, "name")).Save();
            if (scenario == "key")
            {
                command.Patch(update => update.SetProperty(row => row.Id, 4)).AllowWrite(row => row.Id);
            }

            if (scenario == "notVersion")
            {
                command.ExpectVersion<string, CreateCommand>(row => row.Name, (row, request) => request.Name);
            }

            if (scenario == "fetchedVersion")
            {
                command.ExpectVersion<int, CreateCommand>(row => row.Revision, (row, request) => row.Revision);
            }
        }

        Action generate = () => RepositoryModelFactory.Create(context, typeof(CommandContext).Assembly, design: builder.Build());
        generate.Should().Throw<InvalidOperationException>();
    }

    private static RepositoryGenerationModel Model(CommandContext context)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.Commands").UseAsyncContextLeases().GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<CommandRow>();
        entity.ReadEnabled = false;
        entity.BindWritePort<ITypedWrites>();
        entity.HasCommand("Create").Create<CreateCommand>(request => new CommandRow { Name = request.Name, Description = request.Description, OwnerId = request.OwnerId })
            .AllowWrite(row => row.OwnerId).Save().Returns(row => new CommandDto(row.Id, row.Name, row.Description, row.Revision, row.Normalized));
        entity.HasCommand("StageCreate").Create<CreateCommand>(request => new CommandRow { Name = request.Name, Description = request.Description, OwnerId = request.OwnerId })
            .AllowWrite(row => row.OwnerId).Stage();
        entity.HasCommand("FailingResult").Create<CreateCommand>(request => new CommandRow { Name = request.Name, OwnerId = request.OwnerId })
            .AllowWrite(row => row.OwnerId).Save().Returns(row => CommandMapping.Fail(row));
        entity.HasCommand("Patch").Where<PatchCommand>((row, request) => row.Id == request.Id && row.OwnerId == request.OwnerId)
            .Patch(update => update.SetPropertyWhen<string, PatchCommand>(row => row.Name, (row, request) => request.Name.Value!, request => request.Name.IsSpecified)
                .SetPropertyWhen<string?, PatchCommand>(row => row.Description, (row, request) => request.Description.Value, request => request.Description.IsSpecified)
                .SetProperty<int, PatchCommand>(row => row.Revision, (row, request) => request.NewVersion))
            .AllowWrite(row => row.Revision).ExpectVersion<int, PatchCommand>(row => row.Revision, (row, request) => request.ExpectedVersion)
            .Save().ReloadAfterSave().Returns(row => new CommandDto(row.Id, row.Name, row.Description, row.Revision, row.Normalized));
        entity.HasCommand("CompareAndSet").Where<PatchCommand>((row, request) => row.Id == request.Id && row.OwnerId == request.OwnerId)
            .Update(update => update.SetProperty<int, PatchCommand>(row => row.Revision, (row, request) => request.NewVersion))
            .ExpectVersion<int, PatchCommand>(row => row.Revision, (row, request) => request.ExpectedVersion).WithOutcome();
        entity.HasCommand("DeleteSaved").Where<PatchCommand>((row, request) => row.Id == request.Id && row.OwnerId == request.OwnerId)
            .TrackedDelete().ExpectVersion<int, PatchCommand>(row => row.Revision, (row, request) => request.ExpectedVersion)
            .Save().Returns(row => new CommandDto(row.Id, row.Name, row.Description, row.Revision, row.Normalized));
        return RepositoryModelFactory.Create(context, typeof(CommandContext).Assembly, design: builder.Build());
    }

    private static TResult Invoke<TResult>(object target, string method, params object[] arguments) => (TResult)target.GetType().GetMethod(method)!.Invoke(target, arguments)!;

    private sealed class Fixture(CommandContext context, SqliteConnection connection) : IAsyncDisposable
    {
        public CommandContext Context { get; } = context;
        public SqliteConnection Connection { get; } = connection;
        public object Operations(Assembly assembly, IDbContextLeaseProvider<CommandContext> provider) => Activator.CreateInstance(assembly.GetType(Context.GetType().Namespace + ".CommandRowRepositoryOperations")!, provider)!;

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            context.Owners.AddRange(new CommandOwner { Id = 1 }, new CommandOwner { Id = 2 });
            context.Rows.AddRange(new CommandRow { Id = 1, Name = "Initial", Description = "existing", OwnerId = 1 }, new CommandRow { Id = 2, Name = "Other owner", OwnerId = 2 });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return new(context, connection);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class OwnedProvider(SqliteConnection connection) : IDbContextLeaseProvider<CommandContext>
    {
        public List<CommandContext> Contexts { get; } = [];

        public ValueTask<DbContextLease<CommandContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
            Contexts.Add(context);
            return ValueTask.FromResult(DbContextLease.Owned(context));
        }
    }

    private sealed class CommitRecorder : IRepositoryCommitObserver
    {
        public int Calls { get; private set; }

        public Task CommittedAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class VersionRace : SaveChangesInterceptor
    {
        private bool executed;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!executed)
            {
                executed = true;
                await eventData.Context!.Database.ExecuteSqlRawAsync("UPDATE Rows SET Revision = Revision + 1 WHERE Id = 1", cancellationToken);
            }

            return result;
        }
    }
}

public sealed record CreateCommand(string Name, string? Description, int OwnerId);

public sealed record PatchCommand(int Id, int OwnerId, Optional<string> Name, Optional<string?> Description, int ExpectedVersion, int NewVersion);

public sealed record OptionalPatch(Optional<string?> Description);

public sealed record CommandDto(int Id, string Name, string? Description, int Version, string Normalized);

public interface ITypedWrites
{
    Task<CommandResult<CommandDto>> CreateAsync(CreateCommand request, CancellationToken cancellationToken = default);
    Task<CommandResult<CommandDto>> PatchAsync(PatchCommand request, CancellationToken cancellationToken = default);
}

public static class CommandMapping
{
    public static CommandDto Fail(CommandRow row) => throw new InvalidOperationException("Intentional post-save projection failure.");
}

public sealed class CommandOwner
{
    public int Id { get; set; }
}

public sealed class CommandRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int OwnerId { get; set; }
    public CommandOwner? Owner { get; set; }
    public int Revision { get; set; } = 1;
    public string Normalized { get; set; } = "";
}

public class CommandContext(DbContextOptions<CommandContext> options) : DbContext(options)
{
    public DbSet<CommandRow> Rows => Set<CommandRow>();
    public DbSet<CommandOwner> Owners => Set<CommandOwner>();
    public int DisposeCount { get; private set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CommandRow>().HasIndex(row => row.Name).IsUnique();
        modelBuilder.Entity<CommandRow>().Property(row => row.Revision).IsConcurrencyToken();
        modelBuilder.Entity<CommandRow>().Property(row => row.Normalized).HasComputedColumnSql("upper(Name)");
    }

    public override ValueTask DisposeAsync()
    {
        DisposeCount++;
        GC.SuppressFinalize(this);
        return base.DisposeAsync();
    }
}