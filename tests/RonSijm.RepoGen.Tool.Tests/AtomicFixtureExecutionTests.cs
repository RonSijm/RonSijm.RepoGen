using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AtomicFixtureTestHarness;
using static RonSijm.RepoGen.Tool.Tests.ImmediateTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicFixtureExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsertExistingConditionalUpdateAndNoValueOutcomesReuseTheDesignedContract(bool mapped)
    {
        var (_, repository, data) = AtomicFixtureTestHarness.Fixture(mapped);
        var inserted = await Invoke<CommandResult<ImmediateView>>(repository, "EnsureAsync", new AtomicFixtureInput(1));
        inserted.Status.Should().Be(CommandStatus.Inserted); inserted.Value!.Id.Should().Be(5); inserted.HasValue.Should().BeTrue();
        inserted.Value.Stamp[0] = 9;
        var existing = await Invoke<CommandResult<ImmediateView>>(repository, "EnsureAsync", new AtomicFixtureInput(1));
        existing.Status.Should().Be(CommandStatus.AlreadyExists); existing.AffectedRows.Should().Be(0); existing.HasValue.Should().BeFalse();
        var updated = await Invoke<CommandResult<ImmediateView>>(repository, "UpsertAsync", new AtomicFixtureInput(1, "new", Amount: 3));
        updated.Status.Should().Be(CommandStatus.Updated); updated.Value!.Id.Should().Be(5);
        updated.Value.Amount.Should().Be(5); updated.Value.Previous.Should().Be(2); updated.Value.Version.Should().Be(2);
        var stale = await Invoke<CommandResult<ImmediateView>>(repository, "UpsertAsync", new AtomicFixtureInput(1, "new"));
        stale.Status.Should().Be(CommandStatus.NotMatched); stale.HasValue.Should().BeFalse();
        (await Invoke<CommandResult>(repository, "EnsureCountAsync", new AtomicFixtureInput(1, "count"))).Status.Should().Be(CommandStatus.Inserted);
        (await Invoke<CommandResult>(repository, "EnsureCountAsync", new AtomicFixtureInput(1, "count"))).Status.Should().Be(CommandStatus.AlreadyExists);
        data.PendingCount.Should().Be(0); (await data.SaveChangesAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenConflictsRemainConflictsAndPredicatesTokensOnlyConstrainUpdateBranches(bool mapped)
    {
        var (_, repository, _) = AtomicFixtureTestHarness.Fixture(mapped);
        (await Invoke<CommandResult<ImmediateView>>(repository, "EnsureAsync", new AtomicFixtureInput(1, "hidden"))).Status.Should().Be(CommandStatus.AlreadyExists);
        (await Invoke<CommandResult<ImmediateView>>(repository, "UpsertAsync", new AtomicFixtureInput(1, "hidden", Stamp: [4]))).Status.Should().Be(CommandStatus.NotMatched);
        (await Invoke<CommandResult<ImmediateView>>(repository, "UpsertAsync", new AtomicFixtureInput(1, "one", Minimum: 9, Stamp: [1, 2]))).Status.Should().Be(CommandStatus.NotMatched);
        (await Invoke<CommandResult<ImmediateView>>(repository, "UpsertAsync", new AtomicFixtureInput(1, "one", Stamp: [9]))).Status.Should().Be(CommandStatus.NotMatched);
        (await Invoke<CommandResult<ImmediateView>>(repository, "UpsertAsync", new AtomicFixtureInput(1, "one", Stamp: [1, 2]))).Status.Should().Be(CommandStatus.Updated);
        (await Invoke<CommandResult<ImmediateView>>(repository, "UpsertAsync", new AtomicFixtureInput(1, "insert", ExpectedVersion: 999, Minimum: 999, Hidden: true))).Status.Should().Be(CommandStatus.Inserted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingStagingIsNeverFlushedOrRefreshedAndLaterTokenConflictsAreRetained(bool mapped)
    {
        var (_, repository, data) = AtomicFixtureTestHarness.Fixture(mapped);
        await Invoke<CommandResult>(repository, "StageCodeAsync", new AtomicFixtureInput(1, "staged", Id: 1));
        await Invoke<CommandResult<ImmediateView>>(repository, "EnsureAsync", new AtomicFixtureInput(1));
        (await Invoke<IReadOnlyList<ImmediateView>>(repository, "ListAsync", new AtomicFixtureInput(1))).Select(row => row.Code).Should().Contain("one").And.NotContain("staged");
        data.PendingCount.Should().Be(1);
        await Invoke<CommandResult<ImmediateView>>(repository, "UpsertAsync", new AtomicFixtureInput(1, "one", Stamp: [1, 2]));
        Func<Task> save = () => data.SaveChangesAsync(); await save.Should().ThrowAsync<RepositoryWriteConflictException>();
        data.PendingCount.Should().Be(1); data.DiscardChanges();
    }

    [Fact]
    public async Task ValidationNullTargetsAndDuplicateSeedConflictsNeverInventSuccess()
    {
        var (_, repository, data) = AtomicFixtureTestHarness.Fixture();
        Func<Task> invalid = () => Invoke<CommandResult<ImmediateView>>(repository, "EnsureAsync", new AtomicFixtureInput(1, ""));
        await invalid.Should().ThrowAsync<ArgumentException>();
        Func<Task> nullTarget = () => Invoke<CommandResult<ImmediateView>>(repository, "EnsureAsync", new AtomicFixtureInput(1, null!));
        await nullTarget.Should().ThrowAsync<InvalidOperationException>().WithMessage("*conflict target cannot be null*");
        EntityStore(data).Seed(new() { Id = 8, OwnerId = 1, Code = "one", Version = 1 });
        Func<Task> duplicate = () => Invoke<CommandResult<ImmediateView>>(repository, "EnsureAsync", new AtomicFixtureInput(1, "one"));
        await duplicate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Multiple saved rows*");
        EntityStore(data).Count.Should().Be(5);
    }

    [Fact]
    public async Task ContextPartitionsHaveIndependentConflictsAndExplicitGeneratedIds()
    {
        var assembly = AtomicFixtureTestHarness.Compile(AtomicFixtureTestHarness.Builder(mapped: true, keyed: true));
        var (fixture, repository, first) = AtomicFixtureTestHarness.Fixture(mapped: true, assembly: assembly, key: "A");
        var second = Data(fixture, "B"); MappedStore(second).Seed(new(40, 1, "one", 2, 0, false, 1, [1, 2]));
        (await Invoke<CommandResult<ImmediateView>>(repository, "EnsureAsync", new AtomicFixtureInput(1, Partition: "A"))).Value!.Id.Should().Be(5);
        (await Invoke<CommandResult<ImmediateView>>(repository, "EnsureAsync", new AtomicFixtureInput(1, Partition: "B"))).Value!.Id.Should().Be(41);
        MappedStore(first).Count.Should().Be(5); MappedStore(second).Count.Should().Be(2);
    }

    [Fact]
    public async Task ScriptsOverrideTheBackendAndResetRetainsFixtureRowsAndRestoresExecution()
    {
        var (_, repository, data) = AtomicFixtureTestHarness.Fixture();
        var script = repository.GetType().GetProperty("OnEnsureAsync")!.GetValue(repository)!;
        script.GetType().GetMethod("Returns")!.Invoke(script, [new CommandResult<ImmediateView>(CommandStatus.AlreadyExists)]);
        (await Invoke<CommandResult<ImmediateView>>(repository, "EnsureAsync", new AtomicFixtureInput(1))).Status.Should().Be(CommandStatus.AlreadyExists);
        EntityStore(data).Count.Should().Be(4); repository.GetType().GetMethod("ResetTestDouble")!.Invoke(repository, null);
        (await Invoke<CommandResult<ImmediateView>>(repository, "EnsureAsync", new AtomicFixtureInput(1))).Status.Should().Be(CommandStatus.Inserted);
    }

    [Fact]
    public async Task NonStrictNativeCommandsWithoutOptInStillFailExplicitlyRatherThanReturnSuccess()
    {
        var assembly = AtomicFixtureTestHarness.Compile(AtomicFixtureTestHarness.Builder(simulate: false, strict: false));
        var (_, repository, data) = AtomicFixtureTestHarness.Fixture(assembly: assembly);
        Func<Task> execute = () => Invoke<CommandResult<ImmediateView>>(repository, "EnsureAsync", new AtomicFixtureInput(1));
        await execute.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>().WithMessage("*SimulateImmediateWrites*");
        EntityStore(data).Count.Should().Be(4);
    }
}
