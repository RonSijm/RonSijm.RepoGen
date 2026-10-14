using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AdditionalLinkTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AdditionalLinkExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewLinksInitializeReferencesButMatchedLinksKeepTheirOriginalReferences(bool mapped)
    {
        var (repository, data) = Repositories(mapped);
        var result = await Invoke<CommandResult<AdditionalLinkView>>(repository, "ReplaceAsync",
            new AdditionalLinkRequest(1, 1, [new(1, 1, "us", "b", 2, "updated"), new(1, 3, "us", "b", 2, "new")]));
        result.Status.Should().Be(CommandStatus.Applied); result.AffectedRows.Should().Be(4);
        result.Value!.References.Should().Equal("Actor A:Source A", "Actor B:Source B");
        var rows = Rows(data, mapped); rows.Single(row => row.Scope == 1 && row.TargetId == 1).ActorCode.Should().Be("a");
        rows.Single(row => row.Scope == 1 && row.TargetId == 1).SourceId.Should().Be(1);
        rows.Single(row => row.Scope == 1 && row.TargetId == 3).SourceId.Should().Be(2);
        rows.Single(row => row.Scope == 2).Note.Should().Be("other"); rows.Single(row => row.Hidden).Note.Should().Be("hidden");
        (mapped ? Store<AdditionalLinkActorData, int>(data, nameof(AdditionalLinkActor)).Count : Store<AdditionalLinkActor, int>(data, nameof(AdditionalLinkActor)).Count).Should().Be(2);
        (mapped ? Store<AdditionalLinkSourceData, int>(data, nameof(AdditionalLinkSource)).Count : Store<AdditionalLinkSource, int>(data, nameof(AdditionalLinkSource)).Count).Should().Be(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StageSharedSaveBaselineAndRollbackKeepAllFiveStoresConsistent(bool mapped)
    {
        var (repository, data) = Repositories(mapped); data.CaptureBaseline(); var baseline = Rows(data, mapped);
        (await Invoke<CommandResult>(repository, "StageAsync", new AdditionalLinkRequest(1, 1, [new(1, 3, "us", "b", 2, "staged")]))).Status.Should().Be(CommandStatus.Staged);
        Rows(data, mapped).Should().Equal(baseline);
        await Invoke<CommandResult>(repository, "StageNameAsync", 2);
        (await data.SaveChangesAsync()).Should().Be(5);
        var saved = Rows(data, mapped);
        await using (await data.BeginTransactionAsync())
            (await Invoke<CommandResult<AdditionalLinkView>>(repository, "ReplaceAsync", new AdditionalLinkRequest(1, 1, [new(1, 2, "eu", "a", 1, "rollback")], 2))).Status.Should().Be(CommandStatus.Applied);
        Rows(data, mapped).Should().Equal(saved); data.RestoreBaseline(); Rows(data, mapped).Should().Equal(baseline);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("version")]
    [InlineData("linkVersion")]
    [InlineData("missing")]
    public async Task OwnerGuardsVersionsAndMissingResultsCannotWriteAnyStore(string shape)
    {
        var (repository, data) = AdditionalLinkTestHarness.Repositories(); var before = Rows(data, true);
        var request = new AdditionalLinkRequest(shape == "missing" ? 99 : 1, 1,
            [new(shape == "owner" ? 2 : 1, 1, "us", "b", 2, "changed", shape == "linkVersion" ? 99 : 1)], shape == "version" ? 99 : 1);
        if (shape == "owner")
        {
            Func<Task> fail = async () => await Invoke<CommandResult<AdditionalLinkView>>(repository, "ReplaceAsync", request);
            await fail.Should().ThrowAsync<ArgumentException>().WithMessage("*selected owner*");
        }
        else (await Invoke<CommandResult<AdditionalLinkView>>(repository, "ReplaceAsync", request)).Status.Should().Be(shape == "missing" ? CommandStatus.NotFound : CommandStatus.Conflict);
        Rows(data, true).Should().Equal(before); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task FixturesDoNotInventForeignKeyEnforcementOrReferencedEntities()
    {
        var (repository, data) = AdditionalLinkTestHarness.Repositories();
        await Invoke<CommandResult>(repository, "StageAsync", new AdditionalLinkRequest(1, 1, [new(1, 3, "unknown", "actor", 99, "explicit fixture")]));
        (await data.SaveChangesAsync()).Should().Be(4);
        Rows(data, true).Single(row => row.Scope == 1 && row.TargetId == 3).SourceId.Should().Be(99);
        Store<AdditionalLinkActorData, int>(data, nameof(AdditionalLinkActor)).Count.Should().Be(2);
        Store<AdditionalLinkSourceData, int>(data, nameof(AdditionalLinkSource)).Count.Should().Be(2);
    }
}
