using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedStateTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedStateSafetyTests
{
    [Fact]
    public async Task UnsupportedDynamicPropertyReadsStillNeedAcknowledgmentAndNeverReturnEmptySuccess()
    {
        using var context = Context();
        var builder = Builder(strict: false);
        var command = builder.Entity<MappedStateNote>().HasCommand("Dynamic")
            .Where<MappedStateRequest>((row, request) => EF.Property<int>(row, request.Title) > 0)
            .Patch(setters => setters.SetProperty(row => row.Title, "changed")).Save();
        var (repository, _, store) = Fixture(ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build())));
        store.Clear();
        Func<Task> invoke = () => Call<CommandResult>(repository, "DynamicAsync", new MappedStateRequest(1, "Revision"));
        await invoke.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        builder.Configure(options => options.RequirePortableTestCommands());
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        command.RequireTestOverride("Dynamic field selection is outside this fixture's exact mapped scalar contract.");
        var (scripted, _, _) = Fixture(ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build())));
        ScriptedTestDoubleTestHarness.Returns(scripted, "OnDynamicAsync", new CommandResult(CommandStatus.NotFound));
        (await Call<CommandResult>(scripted, "DynamicAsync", new MappedStateRequest(1, "Revision"))).Status.Should().Be(CommandStatus.NotFound);
    }

    [Fact]
    public async Task BoundedCreateBatchesUseCompleteNamedStateInInputOrder()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<MappedStateNote>().HasCommand("CreateMany")
            .CreateMany<MappedStateRequest, IReadOnlyList<MappedStateRequest>>(items => items,
                item => new MappedStateNote { Id = item.Id, Title = item.Title, Counter = 0 }, maximumItems: 3)
            .AllowWrite(row => row.Id).Save().ReturnsSaved(row => EF.Property<int>(row, "Revision"));
        var (repository, _, store) = Fixture(ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build())));
        var result = await Call<CommandResult<IReadOnlyList<int>>>(repository, "CreateManyAsync", (IReadOnlyList<MappedStateRequest>)[new(11, "first"), new(10, "second")]);
        result.Value.Should().Equal(1, 1);
        store.Find(11)!.Title.Should().Be("first");
        store.Find(10)!.Revision.Should().Be(1);
    }

    [Fact]
    public async Task ExplicitAtomicSimulationUsesUnfilteredConflictsAndNamedScalarResults()
    {
        using var context = AtomicFixtureTestHarness.Context();
        var builder = AtomicFixtureTestHarness.Builder(mapped: true);
        builder.Entity<ImmediateRow>().MapInMemoryEntity<ImmediateData>(row => new ImmediateRow
            { Id = row.Key, OwnerId = row.OwnerId, Code = row.Code, Previous = row.Previous, Hidden = row.Hidden, Version = row.Revision, Stamp = row.Marker })
            .MapInMemoryProperty<ImmediateData, int>(nameof(ImmediateRow.Amount), row => row.Amount)
            .MapInMemoryWrites<ImmediateData>((row, saved) => new(row.Id, row.OwnerId, row.Code, row.Amount, row.Previous,
                row.Hidden, row.Version, row.Stamp.ToArray(), saved == null ? "created" : saved.Tag), useMappedPropertyState: true);
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var (_, repository, data) = AtomicFixtureTestHarness.Fixture(mapped: true, assembly: assembly);
        (await Call<CommandResult<ImmediateView>>(repository, "EnsureAsync", new AtomicFixtureInput(1, "hidden"))).Status.Should().Be(CommandStatus.AlreadyExists);
        var inserted = await Call<CommandResult<ImmediateView>>(repository, "EnsureAsync", new AtomicFixtureInput(1));
        inserted.Value!.Amount.Should().Be(2);
        var updated = await Call<CommandResult<ImmediateView>>(repository, "UpsertAsync", new AtomicFixtureInput(1, "new", Amount: 3));
        updated.Value!.Amount.Should().Be(5);
        ImmediateTestHarness.MappedStore(data).Find(inserted.Value.Id)!.Amount.Should().Be(5);
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task NamedFactoriesCanReadDetachedMappedTokensAcrossTheirSameStoreRows()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<MappedStateNote>().WithoutInMemoryGeneratedValue("Revision")
            .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => rows.Select(item => EF.Property<int>(item, "Revision")).DefaultIfEmpty().Max() + 1);
        var (repository, data, store) = Fixture(ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build())));
        data.CaptureBaseline();
        (await Call<CommandResult<MappedStateCard>>(repository, "RenameAsync", new MappedStateRequest(1, "new"))).Value!.Revision.Should().Be(6);
        store.Find(2)!.Revision.Should().Be(5);
        data.RestoreBaseline();
        (await Call<CommandResult<MappedStateCard>>(repository, "RenameAsync", new MappedStateRequest(1, "again"))).Value!.Revision.Should().Be(6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveRechecksOriginalMappedTokensWithoutDiscardingPendingWork(bool binary)
    {
        var (repository, data, store) = Fixture();
        await Call<CommandResult>(repository, "StageRenameAsync", new MappedStateRequest(1, "pending"));
        var original = store.Find(1)!;
        store.Seed(binary ? original with { Stamp = [9] } : original with { Revision = 99 }, InMemoryDuplicateKeyBehavior.Replace);
        Func<Task> save = () => data.SaveChangesAsync();
        await save.Should().ThrowAsync<RepositoryWriteConflictException>();
        data.PendingCount.Should().Be(1);
        store.Find(1)!.Title.Should().Be("old");
    }

    [Fact]
    public async Task FullStructuralRoundTripRejectsLossyMappedStateBeforePublishingAnyRows()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<MappedStateNote>().MapInMemoryWrites<MappedStateSeed>((row, saved) => new(row.Id, row.Title, row.Counter, row.Caption,
            EF.Property<int>(row, "audit"), EF.Property<int>(row, "Revision") + (row.Id == 2 ? 1 : 0), EF.Property<int?>(row, "OptionalVersion"),
            (byte[]?)row["Stamp"], saved == null ? "created" : saved.Extra), useMappedPropertyState: true);
        var (repository, data, store) = Fixture(ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build())));
        await Call<CommandResult>(repository, "StageRenameAsync", new MappedStateRequest(1, "one"));
        await Call<CommandResult>(repository, "StageRenameAsync", new MappedStateRequest(2, "two", 5));
        Func<Task> save = () => data.SaveChangesAsync();
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*round-trip all mapped scalar values*");
        store.Find(1)!.Title.Should().Be("old");
        store.Find(2)!.Title.Should().Be("other");
        data.PendingCount.Should().Be(2);
    }

    [Fact]
    public async Task OrdinarySetterTargetsCanUseNamedScalarSlotsWithoutChangingEqualityPolicies()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<MappedStateNote>().MapInMemoryEntity<MappedStateSeed>(row => new MappedStateNote { Id = row.Id, Counter = row.Counter })
            .MapInMemoryProperty<MappedStateSeed, string>(nameof(MappedStateNote.Title), row => row.Title)
            .HasInMemoryValueEquality(row => row.Title, (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase));
        var (repository, _, store) = Fixture(ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build())));
        (await Call<CommandResult<MappedStateCard>>(repository, "RenameAsync", new MappedStateRequest(1, "new"))).Value!.Title.Should().Be("new");
        store.Find(1)!.Title.Should().Be("new");
        (await Call<int>(repository, "ExecuteRenameAsync", new MappedStateRequest(1, "immediate", 4))).Should().Be(1);
        store.Find(1)!.Title.Should().Be("immediate");
        var batch = await Call<CommandResult<IReadOnlyList<MappedStateCard>>>(repository, "RenameManyAsync",
            (IReadOnlyList<MappedStateRequest>)[new(1, "batch", 5)]);
        batch.Value!.Single().Title.Should().Be("batch");
    }

    [Fact]
    public async Task ScriptsAndPreCanceledCallsDoNotExecuteAutomaticStateOrFactories()
    {
        var (repository, data, store) = Fixture();
        ScriptedTestDoubleTestHarness.Returns(repository, "OnRenameAsync", new CommandResult<MappedStateCard>(CommandStatus.Conflict));
        (await Call<CommandResult<MappedStateCard>>(repository, "RenameAsync", new MappedStateRequest(1, "scripted"))).Status.Should().Be(CommandStatus.Conflict);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Func<Task> invoke = () => (Task)repository.GetType().GetMethod("StageRenameAsync")!
            .Invoke(repository, [new MappedStateRequest(1, "canceled"), cancellation.Token])!;
        await invoke.Should().ThrowAsync<OperationCanceledException>();
        data.PendingCount.Should().Be(0);
        store.Find(1)!.Revision.Should().Be(3);
    }
}
