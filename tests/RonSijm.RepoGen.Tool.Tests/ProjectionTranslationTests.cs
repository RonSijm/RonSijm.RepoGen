using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ProjectionTranslationTestHarness;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProjectionTranslationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DtoHelpersAreExplicitlyExpandedForReadsAndFreshSavedResults(bool mapped)
    {
        var (repository, data) = Fixture(mapped);
        var rows = await Invoke<Task<IReadOnlyList<ProjectionNoteCard>>>(repository, "CardsAsync");
        rows.Select(row => row.Id).Should().Equal(1, 2); rows[0].Caption.Should().Be("ALPHA"); rows[0].Length.Should().Be(7);
        (await Invoke<Task<PagedResult<ProjectionNoteCard>>>(repository, "PageAsync", 2, 1)).Items.Single().Id.Should().Be(2);
        (await Invoke<Task<IReadOnlyList<ProjectionNoteCard>>>(repository, "SourceAsync")).Should().Equal(rows);
        (await Invoke<Task<IReadOnlyList<ProjectionNoteStamp>>>(repository, "StampsAsync")).Select(row => row.Id).Should().Equal(1, 2);
        (await Invoke<Task<IReadOnlyList<ProjectionNoteCard?>>>(repository, "NullableAsync")).Should().OnlyContain(row => row == null);
        (await Invoke<Task<IReadOnlyList<ProjectionNoteGroup>>>(repository, "GroupsAsync")).Single().Should().Be(new ProjectionNoteGroup(false, 2, 2));
        var saved = await Invoke<Task<CommandResult<ProjectionNoteCard>>>(repository, "SavedRenameAsync", new ProjectionNotePatch(1, " renamed ", 1));
        saved.Value!.Caption.Should().Be("RENAMED"); data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OriginalMethodsNeverRunAndMutableDtosRemainDetached(bool mapped)
    {
        var (repository, data) = Fixture(mapped); data.CaptureBaseline();
        (await Invoke<Task<IReadOnlyList<ProjectionNoteCard>>>(repository, "NeverAsync"))[0].Caption.Should().Be(" alpha ");
        var rows = await Invoke<Task<IReadOnlyList<ProjectionNoteData>>>(repository, "MutableAsync"); rows[0].Caption = "edited";
        (await Invoke<Task<IReadOnlyList<ProjectionNoteData>>>(repository, "MutableAsync"))[0].Caption.Should().Be(" alpha ");
        var missing = await Invoke<Task<ProjectionNoteCard?>>(repository, "GetAsync", 999); missing.Should().BeNull();
        (await Invoke<Task<CommandResult<ProjectionNoteCard>>>(repository, "RenameAsync", new ProjectionNotePatch(1, "wrong", 99))).Status.Should().Be(CommandStatus.Conflict);
        data.RestoreBaseline(); (await Invoke<Task<ProjectionNoteCard?>>(repository, "GetAsync", 1))!.Caption.Should().Be("ALPHA");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StageSaveAndInputOrderedBatchResultsRetainExistingWriteSemantics(bool mapped)
    {
        var (repository, data) = Fixture(mapped);
        (await Invoke<Task<CommandResult>>(repository, "StageRenameAsync", new ProjectionNotePatch(1, " staged ", 1))).Status.Should().Be(CommandStatus.Staged);
        (await Invoke<Task<ProjectionNoteCard?>>(repository, "GetAsync", 1))!.Caption.Should().Be("ALPHA");
        (await data.SaveChangesAsync()).Should().Be(1);
        (await Invoke<Task<ProjectionNoteCard?>>(repository, "GetAsync", 1))!.Caption.Should().Be("STAGED");
        IReadOnlyList<ProjectionNotePatch> input = [new(2, "second", 2), new(1, "first", 1)];
        var batch = await Invoke<Task<CommandResult<IReadOnlyList<ProjectionNoteCard>>>>(repository, "RenameManyAsync", input);
        batch.Value!.Select(row => row.Id).Should().Equal(2, 1); batch.Value!.Select(row => row.Caption).Should().Equal("SECOND", "FIRST");
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task DtoStreamsKeepColdCoordinatedSnapshotsAndCancellation()
    {
        var (repository, data) = Fixture();
        var stream = Invoke<IAsyncEnumerable<ProjectionNoteCard>>(repository, "StreamAsync");
        Store<TranslatedWriteSeed>(data).Seed(new(1, " before ", 1, false), InMemoryDuplicateKeyBehavior.Replace);
        await using var enumerator = stream.GetAsyncEnumerator(); (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Caption.Should().Be("BEFORE");
        Store<TranslatedWriteSeed>(data).Seed(new(2, "after", 2, false), InMemoryDuplicateKeyBehavior.Replace);
        (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Caption.Should().Be("BETA");
        (await Invoke<Task<ProjectionNoteCard?>>(repository, "GetAsync", 2))!.Caption.Should().Be("AFTER");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await using var canceled = stream.GetAsyncEnumerator(cancellation.Token);
        Func<Task> move = async () => await canceled.MoveNextAsync(); await move.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task UnmatchedFixedCallShapesStillFailOnEmptyStoresInsteadOfReturningAnEmptySuccess()
    {
        var (repository, data) = Fixture(); data.Clear();
        Func<Task> query = async () => await Invoke<Task<IReadOnlyList<ProjectionNoteCard>>>(repository, "WrongLiteralAsync");
        await query.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
    }
}
