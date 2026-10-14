using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ImmediateTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

[Collection("Proxy writes")]
public sealed class ImmediateWriteExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalRowSettersVisibilityAndReturnedValuesMatchTheDesignedOperation(bool mapped)
    {
        var (_, repository, data) = Fixture(mapped);
        var result = await Invoke<MutationResult<ImmediateView>>(repository, "BumpAsync", new ImmediateRequest(1, Delta: 5));
        result.Items.OrderBy(row => row.Id).Select(row => (row.Amount, row.Previous, row.Version)).Should().Equal((7, 2, 2), (8, 3, 2));
        var rows = await Invoke<IReadOnlyList<ImmediateView>>(repository, "ListAsync", new ImmediateRequest(1));
        rows.Select(row => row.Amount).Should().Equal(7, 8);
        if (mapped)
        {
            MappedStore(data).Find(3)!.Amount.Should().Be(4); MappedStore(data).Find(4)!.Amount.Should().Be(5);
            MappedStore(data).Snapshot().Should().OnlyContain(row => row.Tag == "arranged");
        }
        else { EntityStore(data).Find(3)!.Amount.Should().Be(4); EntityStore(data).Find(4)!.Amount.Should().Be(5); }
        result.Items[0].Stamp[0] = 99;
        (await Invoke<IReadOnlyList<ImmediateView>>(repository, "ListAsync", new ImmediateRequest(1)))[0].Stamp[0].Should().Be(1);
        data.PendingCount.Should().Be(0); (await data.SaveChangesAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsumeUsesInputVersionsAndCanOnlyReturnSavedRowsOnce(bool mapped)
    {
        var (_, repository, data) = Fixture(mapped);
        (await Invoke<MutationResult<ImmediateView>>(repository, "ConsumeAsync", new ImmediateRequest(1, "one", ExpectedVersion: 0))).AffectedRows.Should().Be(0);
        var consumed = await Invoke<MutationResult<ImmediateView>>(repository, "ConsumeAsync", new ImmediateRequest(1, "one"));
        consumed.Items.Single().Amount.Should().Be(2);
        (await Invoke<MutationResult<ImmediateView>>(repository, "ConsumeAsync", new ImmediateRequest(1, "one"))).AffectedRows.Should().Be(0);
        (await Invoke<MutationResult<ImmediateView>>(repository, "ConsumeAsync", new ImmediateRequest(1, "hidden"))).AffectedRows.Should().Be(0);
        if (mapped) MappedStore(data).Count.Should().Be(3); else EntityStore(data).Count.Should().Be(3);
    }

    [Fact]
    public async Task ExplicitBinaryExpectedVersionsUseStructuralValuesRatherThanArrayIdentity()
    {
        var (_, repository, data) = Fixture(mapped: true);
        (await Invoke<MutationResult<ImmediateView>>(repository, "ConsumeStampAsync", new ImmediateStampRequest(1, [9]))).AffectedRows.Should().Be(0);
        (await Invoke<MutationResult<ImmediateView>>(repository, "ConsumeStampAsync", new ImmediateStampRequest(1, [1, 2]))).AffectedRows.Should().Be(1);
        MappedStore(data).Find(1).Should().BeNull();
    }

    [Fact]
    public async Task CountAndOutcomeCommandsKeepNoMatchDistinctFromTrackedNotFoundOrConflict()
    {
        var (_, repository, _) = Fixture();
        (await Invoke<int>(repository, "CountUpdateAsync", new ImmediateRequest(1))).Should().Be(2);
        (await Invoke<CommandResult>(repository, "CompareAndSetAsync", new ImmediateRequest(1, "one", ExpectedVersion: 0))).Status.Should().Be(CommandStatus.NotMatched);
        var applied = await Invoke<CommandResult>(repository, "CompareAndSetAsync", new ImmediateRequest(1, "one"));
        applied.Status.Should().Be(CommandStatus.Applied); applied.AffectedRows.Should().Be(1);
        (await Invoke<int>(repository, "CountDeleteAsync", new ImmediateRequest(1, "hidden"))).Should().Be(0);
        (await Invoke<int>(repository, "CountDeleteAsync", new ImmediateRequest(1, "two"))).Should().Be(1);
    }

    [Fact]
    public async Task GeneratedGuardsAndOverflowDoNotPartiallyCommit()
    {
        var (_, repository, data) = Fixture();
        Func<Task> invalid = () => Invoke<MutationResult<ImmediateView>>(repository, "BumpAsync", new ImmediateRequest(1, Delta: 0));
        await invalid.Should().ThrowAsync<ArgumentException>();
        Func<Task> overflow = () => Invoke<MutationResult<ImmediateView>>(repository, "BumpAsync", new ImmediateRequest(1, Delta: int.MaxValue));
        await overflow.Should().ThrowAsync<OverflowException>();
        EntityStore(data).Find(1)!.Amount.Should().Be(2); EntityStore(data).Find(2)!.Amount.Should().Be(3);
    }

    [Fact]
    public async Task ImmediateWritesLeaveStagedStatePendingAndStaleTokensFailTheLaterSave()
    {
        var (_, repository, data) = Fixture();
        await Invoke<CommandResult>(repository, "StageCodeAsync", new ImmediateRequest(1, "staged", Delta: 1));
        await Invoke<MutationResult<ImmediateView>>(repository, "BumpAsync", new ImmediateRequest(1));
        EntityStore(data).Find(1)!.Code.Should().Be("one"); data.PendingCount.Should().Be(1);
        Func<Task> save = () => data.SaveChangesAsync(); await save.Should().ThrowAsync<RepositoryWriteConflictException>();
        data.DiscardChanges(); EntityStore(data).Find(1)!.Amount.Should().Be(3);
    }

    [Fact]
    public async Task ExplicitComputedFactoriesAreUsedWithoutRunningPersistenceConstructors()
    {
        using var context = ProxyWriteTestHarness.Context(); var builder = ProxyWriteTestHarness.Builder();
        builder.Configure(options => options.UseReturningWrites<RonSijm.RepoGen.Sqlite.SqliteReturningWriter>());
        var entity = ProxyWriteRepositoryDesigner.Store(builder);
        entity.HasCommand("Shorten").Where(row => row.Id == 7).Update(setters => setters.SetProperty(row => row.Name, "X"))
            .SimulateImmediateWrites().ReturnsMany(row => new ProxyWriteView(row.Id, row.Name, row.Version, row.Label, row.Stamp.ToArray()));
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.ProxyWrites.TestDoubles.ProxyWriteContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.ProxyWrites.TestDoubles.ProxyWriteRecordRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = (InMemoryStore<ProxyWriteData, int>)data.GetType().GetProperty("ProxyWriteRecordStore")!.GetValue(data)!;
        store.Seed(new(7, "old", 3, "fixture", false, [1, 2]));
        var constructions = ProxyWriteRecord.Constructions;
        var result = await ProxyWriteTestHarness.Invoke<MutationResult<ProxyWriteView>>(repository, "ShortenAsync");
        result.Items.Single().Version.Should().Be(1); store.Find(7)!.Revision.Should().Be(1); data.PendingCount.Should().Be(0);
        ProxyWriteRecord.Constructions.Should().Be(constructions);
    }

    [Fact]
    public async Task ImmediateWritesStayInsideTheDesignedContextPartition()
    {
        var builder = Builder(mapped: true, keyed: true); var assembly = Compile(builder);
        var (fixture, repository, first) = Fixture(mapped: true, assembly: assembly, key: "A");
        var second = Data(fixture, "B"); MappedStore(second).Seed(new(1, 1, "one", 20, 0, false, 1, [1, 2]));
        await Invoke<MutationResult<ImmediateView>>(repository, "BumpAsync", new ImmediateRequest(1, Partition: "A"));
        MappedStore(first).Find(1)!.Amount.Should().Be(3); MappedStore(second).Find(1)!.Amount.Should().Be(20);
        (await Invoke<MutationResult<ImmediateView>>(repository, "ConsumeAsync", new ImmediateRequest(1, "one", Partition: "B"))).AffectedRows.Should().Be(1);
        MappedStore(first).Find(1).Should().NotBeNull(); MappedStore(second).Find(1).Should().BeNull();
    }

    [Fact]
    public async Task ExplicitScriptsOverrideImmediateBackendsAndResetRestoresTheBackend()
    {
        var (_, repository, data) = Fixture(); var script = repository.GetType().GetProperty("OnConsumeAsync")!.GetValue(repository)!;
        script.GetType().GetMethod("Returns")!.Invoke(script, [new MutationResult<ImmediateView>([])]);
        (await Invoke<MutationResult<ImmediateView>>(repository, "ConsumeAsync", new ImmediateRequest(1, "one"))).AffectedRows.Should().Be(0);
        EntityStore(data).Find(1).Should().NotBeNull(); repository.GetType().GetMethod("ResetTestDouble")!.Invoke(repository, null);
        (await Invoke<MutationResult<ImmediateView>>(repository, "ConsumeAsync", new ImmediateRequest(1, "one"))).AffectedRows.Should().Be(1);
    }
}
