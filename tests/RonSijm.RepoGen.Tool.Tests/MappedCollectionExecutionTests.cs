using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedCollectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedCollectionExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteScalarStateSupportsOwnedAddUpdateDeleteAndGetterFreeNamedTokens(bool textSlot)
    {
        var (parent, _, data) = Fixture(textSlot ? Compile(Builder(textSlot: true)) : null);
        MappedCollectionChild.GetterCalls.Value = 0;
        var result = await Call<CommandResult<int>>(parent, "Replace", Request());
        result.Status.Should().Be(CommandStatus.Applied);
        result.Value.Should().Be(4);
        ParentStore(data).Find(1)!.Revision.Should().Be(4);
        ChildStore(data).Find(1)!.Should().BeEquivalentTo(Seeds()[0] with { Text = "changed a", Counter = 3, Revision = 4 });
        ChildStore(data).Find(2).Should().BeNull();
        ChildStore(data).Find(3)!.Text.Should().Be("hidden");
        ChildStore(data).Find(4)!.Text.Should().Be("other");
        var created = ChildStore(data).Find(20)!;
        created.ParentId.Should().Be(1);
        created.Revision.Should().Be(1);
        created.Category.Should().Be("entry");
        created.Audit.Should().Be(10);
        created.OptionalVersion.Should().Be(1);
        created.Stamp.Should().Equal(1, 2);
        created.Extra.Should().Be("created");
        data.PendingCount.Should().Be(0);
        MappedCollectionChild.GetterCalls.Value.Should().Be(0);
    }

    [Fact]
    public async Task StagedMappedChildrenStayPendingAndSharedSaveAlsoIncludesUnrelatedWork()
    {
        var (parent, child, data) = Fixture();
        await Call<CommandResult>(child, "StageText", 4);
        (await Call<CommandResult>(parent, "Stage", Request())).Status.Should().Be(CommandStatus.Staged);
        ChildStore(data).Find(20).Should().BeNull();
        ChildStore(data).Find(1)!.Revision.Should().Be(3);
        ParentStore(data).Find(1)!.Revision.Should().Be(3);
        await data.SaveChangesAsync();
        ChildStore(data).Find(1)!.Revision.Should().Be(4);
        ChildStore(data).Find(4)!.Text.Should().Be("independent");
        ChildStore(data).Find(20)!.Revision.Should().Be(1);
        ParentStore(data).Find(1)!.Revision.Should().Be(4);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("child")]
    [InlineData("nullable")]
    [InlineData("binary")]
    public async Task WrongOriginalVersionsRejectTheWholeCommandBeforeStaging(string token)
    {
        var (parent, _, data) = Fixture();
        var request = Request();
        var item = request.Items[0];
        request = token == "parent" ? request with { Revision = 99 } : request with
        {
            Items = [token == "child" ? item with { Revision = 99 } : token == "nullable" ? item with { OptionalVersion = null } : item with { Stamp = [9] }, request.Items[1]]
        };
        (await Call<CommandResult<int>>(parent, "Replace", request)).Status.Should().Be(CommandStatus.Conflict);
        ParentStore(data).Find(1)!.Title.Should().Be("old");
        ChildStore(data).Find(1)!.Text.Should().Be("old a");
        ChildStore(data).Find(20).Should().BeNull();
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task NullableBinaryNullsAndConditionalMappedSettersKeepTheirDeclaredSemantics()
    {
        var (parent, _, data) = Fixture(Compile(Builder(textSlot: true)));
        var request = new MappedCollectionRequest(1, "changed", [new(2, "b", "must not replace", 5, null, null, Supplied: false)]);
        (await Call<CommandResult<int>>(parent, "Merge", request)).Status.Should().Be(CommandStatus.Applied);
        var row = ChildStore(data).Find(2)!;
        row.Text.Should().Be("old b");
        row.Revision.Should().Be(6);
        row.OptionalVersion.Should().BeNull();
        row.Stamp.Should().BeNull();
        ChildStore(data).Find(1).Should().NotBeNull();
    }

    [Fact]
    public async Task HiddenOwnerScopedPendingRowsAreStillRejectedAndRetained()
    {
        var (parent, child, data) = Fixture();
        await Call<CommandResult>(child, "StageText", 3);
        Func<Task> execute = () => Call<CommandResult<int>>(parent, "Replace", Request());
        await execute.Should().ThrowAsync<InvalidOperationException>();
        data.PendingCount.Should().Be(1);
        ParentStore(data).Find(1)!.Revision.Should().Be(3);
        ChildStore(data).Find(20).Should().BeNull();
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("child")]
    [InlineData("binary")]
    public async Task SharedSaveRechecksOriginalMappedVersionsBeforeAnyStoreCommits(string token)
    {
        var (parent, _, data) = Fixture();
        await Call<CommandResult>(parent, "Stage", Request());
        if (token == "parent")
        {
            ParentStore(data).Seed(ParentStore(data).Find(1)! with { Revision = 99 }, InMemoryDuplicateKeyBehavior.Replace);
        }
        else
        {
            var row = ChildStore(data).Find(1)!;
            ChildStore(data).Seed(token == "child" ? row with { Revision = 99 } : row with { Stamp = [9] }, InMemoryDuplicateKeyBehavior.Replace);
        }
        Func<Task> save = () => data.SaveChangesAsync();
        await save.Should().ThrowAsync<RepositoryWriteConflictException>();
        data.PendingCount.Should().BeGreaterThan(0);
        ParentStore(data).Find(1)!.Counter.Should().Be(2);
        ChildStore(data).Find(1)!.Counter.Should().Be(2);
        ChildStore(data).Find(2).Should().NotBeNull();
        ChildStore(data).Find(20).Should().BeNull();
    }

    [Fact]
    public async Task SavedGraphReadsUseFreshMappedValuesWithIndependentReadPermission()
    {
        var (parent, _, _) = Fixture();
        var result = await Call<CommandResult<MappedCollectionView>>(parent, "Saved", Request());
        result.Value!.Revision.Should().Be(4);
        result.Value.Items.Select(item => item.Id).Should().Equal(1, 20);
        result.Value.Items.Select(item => item.Revision).Should().Equal(4, 1);
        result.Value.Items.Should().OnlyContain(item => item.Category == "entry" && item.Audit == 10);
    }

    [Fact]
    public async Task CheckpointRollbackAndBaselineRestoreIncludeEveryMappedParticipant()
    {
        var (parent, _, data) = Fixture();
        data.CaptureBaseline();
        await using (var checkpoint = await data.BeginTransactionAsync())
        {
            await Call<CommandResult<int>>(parent, "Replace", Request());
        }
        ParentStore(data).Find(1)!.Revision.Should().Be(3);
        ChildStore(data).Find(2).Should().NotBeNull();
        ChildStore(data).Find(20).Should().BeNull();
        await Call<CommandResult<int>>(parent, "Replace", Request());
        data.RestoreBaseline();
        ParentStore(data).Find(1)!.Revision.Should().Be(3);
        ChildStore(data).Find(1)!.Stamp.Should().Equal(1, 2);
        ChildStore(data).Find(20).Should().BeNull();
    }
}
