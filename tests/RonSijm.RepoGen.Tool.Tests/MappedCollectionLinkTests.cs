using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedCollectionLinkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitLinksReuseMappedNonIdentityStateWithoutChangingSharedReferenceGuards(bool wrongOwner)
    {
        using var context = SharedLinkTestHarness.Context();
        var builder = SharedLinkTestHarness.Builder();
        builder.Entity<SharedLinkRow>()
            .MapInMemoryEntity<SharedLinkRowData>(row => new SharedLinkRow { Scope = row.Scope, OwnerId = row.OwnerId, TargetId = row.TargetId, Hidden = row.Hidden })
            .MapInMemoryProperty<SharedLinkRowData, string>(nameof(SharedLinkRow.Note), row => row.Note)
            .MapInMemoryProperty<SharedLinkRowData, int>(nameof(SharedLinkRow.Version), row => row.Version)
            .MapInMemoryWrites<SharedLinkRowData>((row, saved) => new(row.Scope, row.OwnerId, row.TargetId, row.Note, row.Version, row.Hidden), useMappedPropertyState: true);
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var (repository, data) = SharedLinkTestHarness.Repositories(mapped: true, assembly: assembly);
        var request = new SharedLinkRequest(1, 1, [new(1, 1, "changed"), new(wrongOwner ? 2 : 1, 3, "created")]);
        if (wrongOwner)
        {
            Func<Task> execute = () => MemoryWriteTestHarness.Invoke<CommandResult<SharedLinkView>>(repository, "ReplaceAsync", request);
            await execute.Should().ThrowAsync<ArgumentException>();
            SharedLinkTestHarness.Rows(data, true).Single(row => row.Scope == 1 && row.TargetId == 1).Version.Should().Be(1);
            data.PendingCount.Should().Be(0);
        }
        else
        {
            var result = await MemoryWriteTestHarness.Invoke<CommandResult<SharedLinkView>>(repository, "ReplaceAsync", request);
            result.Status.Should().Be(CommandStatus.Applied);
            result.Value!.Notes.Should().Equal("changed", "created");
            var rows = SharedLinkTestHarness.Rows(data, true);
            rows.Single(row => row.Scope == 1 && row.TargetId == 1).Version.Should().Be(2);
            rows.Single(row => row.Scope == 1 && row.TargetId == 3).Version.Should().Be(1);
            rows.Single(row => row.Scope == 2).Note.Should().Be("other");
            SharedLinkTestHarness.Store<SharedLinkTargetData, (int, int)>(data, nameof(SharedLinkTarget)).Count.Should().Be(5);
        }
    }
}
