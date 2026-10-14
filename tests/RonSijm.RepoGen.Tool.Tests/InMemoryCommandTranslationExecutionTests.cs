using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryCommandTranslationTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryCommandTranslationExecutionTests
{
    private static Task<TResult> Batch<TResult>(object repository, string name, IReadOnlyList<MemoryWriteRequest> items)
        => Invoke<TResult>(repository, name, (object)items);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StagedWritesGuardsSettersVersionsAndSavedResultsUseDeclaredTranslations(bool mapped)
    {
        var (repository, _, data) = Fixture(mapped);
        (await Invoke<CommandResult>(repository, "StageRenameAsync", new MemoryWriteRequest(1, " staged "))).Status.Should().Be(CommandStatus.Staged);
        Rows(data, mapped)[0].Title.Should().Be("old"); data.PendingCount.Should().Be(1);
        (await data.SaveChangesAsync()).Should().Be(1); Rows(data, mapped)[0].Should().Be(new TranslatedWriteSeed(1, "STAGED", 2));
        var stale = await Invoke<CommandResult<MemoryWriteView>>(repository, "RenameAsync", new MemoryWriteRequest(1, "stale"));
        stale.Status.Should().Be(CommandStatus.Conflict);
        (await Invoke<CommandResult<MemoryWriteView>>(repository, "RenameAsync", new MemoryWriteRequest(2, "hidden"))).Status.Should().Be(CommandStatus.NotFound);
        (await Invoke<CommandResult<MemoryWriteView>>(repository, "CreateAsync", new MemoryWriteRequest(4, " new "))).Value.Should().Be(new MemoryWriteView(4, "NEW", 1));
        var saved = await Invoke<CommandResult<MemoryWriteView>>(repository, "SavedRenameAsync", new MemoryWriteRequest(4, " saved "));
        saved.Value.Should().Be(new MemoryWriteView(4, "SAVED", 5)); Rows(data, mapped).Single(row => row.Id == 4).Version.Should().Be(2);
        (await Invoke<CommandResult<MemoryWriteView>>(repository, "DeleteAsync", new MemoryWriteRequest(4, "", 2))).Value.Should().Be(new MemoryWriteView(4, "SAVED", 2));
        Rows(data, mapped).Should().NotContain(row => row.Id == 4);
        Func<Task> guard = () => Invoke<CommandResult<MemoryWriteView>>(repository, "CreateAsync", new MemoryWriteRequest(0, "bad"));
        await guard.Should().ThrowAsync<ArgumentException>(); data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchInputsMatchingKeysConditionsCreatesAndSavedResultsRetainBoundedAtomicSemantics(bool mapped)
    {
        var (repository, _, data) = Fixture(mapped);
        var created = await Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(repository, "CreateManyAsync", new MemoryWriteRequest[] { new(5, " fifth "), new(-1, "filtered"), new(4, "fourth") });
        created.Value.Should().Equal(new MemoryWriteView(5, "FIFTH", 5), new MemoryWriteView(4, "FOURTH", 6));
        var patch = await Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(repository, "RenameManyAsync", new MemoryWriteRequest[] { new(3, " changed "), new(1, "ignored", Supplied: false) });
        patch.Value.Should().Equal(new MemoryWriteView(3, "CHANGED", 7), new MemoryWriteView(1, "old", 3));
        var baseline = Rows(data, mapped);
        var conflict = await Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(repository, "RenameManyAsync", new MemoryWriteRequest[] { new(1, "bad", 2), new(3, "stale", 1) });
        conflict.Status.Should().Be(CommandStatus.Conflict); Rows(data, mapped).Should().Equal(baseline);
        (await Batch<CommandResult>(repository, "StageManyAsync", new MemoryWriteRequest[] { new(4, "stage four"), new(5, "stage five") })).Status.Should().Be(CommandStatus.Staged);
        Rows(data, mapped).Should().Equal(baseline); (await data.SaveChangesAsync()).Should().Be(2);
        var deleted = await Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(repository, "DeleteManyAsync", new MemoryWriteRequest[] { new(5, "", 2), new(4, "", 2) });
        deleted.Value!.Select(row => row.Id).Should().Equal(5, 4);
        Func<Task> tooMany = () => Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(repository, "CreateManyAsync", new MemoryWriteRequest[] { new(6, "a"), new(7, "b"), new(8, "c"), new(9, "d") });
        await tooMany.Should().ThrowAsync<ArgumentException>().WithMessage("*item limit*"); data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyedContextsTranslateOnlyTheDeclaredScalarKeyAndCancellationAndScriptsStillWin(bool mapped)
    {
        var assembly = Compile(Builder(mapped, keyed: true)); var (repository, fixture, data) = Fixture(mapped, assembly, "A");
        (await Invoke<CommandResult<MemoryWriteView>>(repository, "RenameAsync", new MemoryWriteRequest(1, " key ", ContextKey: " a "))).Value.Should().Be(new MemoryWriteView(1, "KEY", 2));
        var other = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, ["B"])!;
        Rows(other, mapped).Should().BeEmpty();
        ScriptedTestDoubleTestHarness.Returns(repository, "OnRenameAsync", new CommandResult<MemoryWriteView>(CommandStatus.Applied, 1, new(99, "script", 1)));
        (await Invoke<CommandResult<MemoryWriteView>>(repository, "RenameAsync", new MemoryWriteRequest(1, "ignored"))).Value!.Id.Should().Be(99);
        Rows(data, mapped)[0].Title.Should().Be("KEY");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Func<Task> operation = () => (Task)repository.GetType().GetMethod("SavedRenameAsync")!.Invoke(repository, [new MemoryWriteRequest(1, "bad", 2), canceled.Token])!;
        await operation.Should().ThrowAsync<OperationCanceledException>(); data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnmatchedCallsFailEvenOnEmptyStoresAndOverridesNeverBecomeAutomatic(bool mapped)
    {
        var (repository, _, data) = Fixture(mapped);
        if (mapped) Store<TranslatedWriteSeed>(data).Clear(); else Store<MemoryWriteNote>(data).Clear();
        Func<Task> unsupported = () => Invoke<CommandResult>(repository, "UnknownAsync", new MemoryWriteRequest(1, "ignored"));
        await unsupported.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        ScriptedTestDoubleTestHarness.Returns(repository, "OnUnknownAsync", new CommandResult(CommandStatus.Staged, 1));
        (await Invoke<CommandResult>(repository, "UnknownAsync", new MemoryWriteRequest(1, "ignored"))).Status.Should().Be(CommandStatus.Staged);
        data.PendingCount.Should().Be(0);
    }
}
