using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NativeSavedTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NativeSavedExecutionTests
{
    [Fact]
    public async Task FreshGraphsSeeEveryUpdatedRootAndIgnoreStaleVersionPredicates()
    {
        var (repository, _, data) = Fixture();
        var result = await Call<MutationResult<NativeSavedView>>(repository, "Rewrite", new NativeSavedRequest(1));
        result.AffectedRows.Should().Be(2);
        result.Items.Should().BeEquivalentTo(new[]
        {
            new NativeSavedView(1, "changed:a", 2, "saved parent", "changed:", ["changed:a", "changed:b"]),
            new NativeSavedView(2, "changed:b", 2, "saved parent", "changed:", ["changed:a", "changed:b"])
        });
        MemoryCollectionTestHarness.ChildStore(data).Find(3)!.Text.Should().Be("secret");
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task UnmatchedRowsReturnAnEmptyResultWithoutReadingExistingData()
    {
        var (repository, _, data) = Fixture();
        var before = MemoryCollectionTestHarness.ChildStore(data).Snapshot();
        var result = await Call<MutationResult<NativeSavedView>>(repository, "Rewrite", new NativeSavedRequest(1, ExpectedVersion: 99));
        result.AffectedRows.Should().Be(0);
        result.Items.Should().BeEmpty();
        MemoryCollectionTestHarness.ChildStore(data).Snapshot().Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData("TightBound", false)]
    [InlineData("Fail", false)]
    [InlineData("Hide", true)]
    public async Task BoundProjectionOrVisibilityFailurePublishesNoPartialRows(string operation, bool hidden)
    {
        var (repository, parent, data) = Fixture();
        await Call<CommandResult>(parent, "StageName", 1);
        var before = MemoryCollectionTestHarness.ChildStore(data).Snapshot();
        var request = new NativeSavedRequest(1, Hidden: hidden);
        Func<Task> failure = () => operation == "Fail"
            ? Call<MutationResult<SavedResultRejectedView>>(repository, operation, request)
            : Call<MutationResult<NativeSavedView>>(repository, operation, request);
        await failure.Should().ThrowAsync<InvalidOperationException>();
        MemoryCollectionTestHarness.ChildStore(data).Snapshot().Should().BeEquivalentTo(before);
        data.PendingCount.Should().Be(1);
        await data.SaveChangesAsync();
        MemoryCollectionTestHarness.ParentStore(data).Find(1)!.Name.Should().Be("pending parent");
    }

    [Fact]
    public async Task ExplicitBypassAppliesToFreshRootsButNotToProjectionOwnedRelatedVisibility()
    {
        var (repository, _, _) = Fixture();
        var result = await Call<MutationResult<NativeSavedView>>(repository, "BypassHide", new NativeSavedRequest(1, Hidden: true));
        result.AffectedRows.Should().Be(3);
        result.Items.Should().OnlyContain(item => item.Siblings.Count == 0 && item.Version == 2);
    }

    [Fact]
    public async Task FreshSnapshotsRetainStagingAndReadSavedOnlyRelatedRows()
    {
        var (repository, parent, data) = Fixture();
        await Call<CommandResult>(parent, "StageName", 1);
        await Call<CommandResult>(repository, "StageText", 4);
        var result = await Call<MutationResult<NativeSavedView>>(repository, "Rewrite", new NativeSavedRequest(1));
        result.Items.Should().OnlyContain(item => item.Parent == "saved parent");
        data.PendingCount.Should().Be(2);
        await data.SaveChangesAsync();
        MemoryCollectionTestHarness.ChildStore(data).Find(4)!.Text.Should().Be("pending child");
        MemoryCollectionTestHarness.ParentStore(data).Find(1)!.Name.Should().Be("pending parent");
    }

    [Fact]
    public async Task ASeparateSharedSaveStillChecksRetainedOriginalTokens()
    {
        var (repository, _, data) = Fixture();
        await Call<CommandResult>(repository, "StageText", 1);
        await Call<MutationResult<NativeSavedView>>(repository, "Rewrite", new NativeSavedRequest(1));
        Func<Task> save = () => data.SaveChangesAsync();
        await save.Should().ThrowAsync<RepositoryWriteConflictException>();
        data.PendingCount.Should().Be(1);
        MemoryCollectionTestHarness.ChildStore(data).Find(1)!.Version.Should().Be(2);
    }

    [Fact]
    public async Task ResultsBaselinesAndCheckpointRollbackRemainDetached()
    {
        var (repository, _, data) = Fixture();
        data.CaptureBaseline();
        await using (var transaction = await data.BeginTransactionAsync())
        {
            var result = await Call<MutationResult<NativeSavedView>>(repository, "Rewrite", new NativeSavedRequest(1));
            ((List<string>)result.Items[0].Siblings).Clear();
            MemoryCollectionTestHarness.ChildStore(data).Find(1)!.Text.Should().Be("changed:a");
            await transaction.RollbackAsync();
        }
        MemoryCollectionTestHarness.ChildStore(data).Find(1)!.Text.Should().Be("A");
        await Call<MutationResult<NativeSavedView>>(repository, "Rewrite", new NativeSavedRequest(1));
        data.RestoreBaseline();
        MemoryCollectionTestHarness.ChildStore(data).Find(1)!.Text.Should().Be("A");
    }

    [Fact]
    public async Task KeyedFixturesQueryOnlyTheirSelectedPartition()
    {
        var assembly = Compile(Builder(keyed: true));
        var (repository, _, data) = Fixture(assembly, "first");
        var result = await Call<MutationResult<NativeSavedView>>(repository, "Rewrite", new NativeSavedRequest(1));
        result.AffectedRows.Should().Be(2);
        var empty = await Call<MutationResult<NativeSavedView>>(repository, "Rewrite", new NativeSavedRequest(1, Partition: "other"));
        empty.Items.Should().BeEmpty();
        MemoryCollectionTestHarness.ChildStore(data).Find(1)!.Text.Should().Be("changed:a");
    }
}
