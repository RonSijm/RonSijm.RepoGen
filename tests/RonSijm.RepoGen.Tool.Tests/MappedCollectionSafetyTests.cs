using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedCollectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedCollectionSafetyTests
{
    [Fact]
    public async Task LossyChildWriteMapCannotCommitTheParentOrAnySiblingStore()
    {
        var builder = Builder();
        builder.Entity<MappedCollectionChild>().MapInMemoryWrites<MappedCollectionChildSeed>((row, saved) => new(row.Id, row.ParentId, row.Code, row.Text,
            row.Counter, row.Category, EF.Property<int>(row, "audit"), EF.Property<int>(row, "Revision") + (row.Text == "changed a" ? 1 : 0),
            EF.Property<int?>(row, "OptionalVersion"), (byte[]?)row["Stamp"], saved == null ? "created" : saved.Extra), useMappedPropertyState: true);
        var (parent, child, data) = Fixture(Compile(builder));
        await Call<CommandResult>(child, "StageText", 4);
        Func<Task> save = () => Call<CommandResult<int>>(parent, "Replace", Request());
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*round-trip*");
        ParentStore(data).Find(1)!.Revision.Should().Be(3);
        ChildStore(data).Find(1)!.Revision.Should().Be(3);
        ChildStore(data).Find(2).Should().NotBeNull();
        ChildStore(data).Find(20).Should().BeNull();
        data.PendingCount.Should().Be(1);
        await data.SaveChangesAsync();
        ChildStore(data).Find(4)!.Text.Should().Be("independent");
    }

    [Fact]
    public async Task NamedChildFactoryFailureRestoresThisCommandAndKeepsPriorUnrelatedStaging()
    {
        var builder = Builder();
        builder.Entity<MappedCollectionChild>().WithoutInMemoryGeneratedValue("Revision")
            .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => checked(row.Counter + int.MaxValue));
        var (parent, child, data) = Fixture(Compile(builder));
        await Call<CommandResult>(child, "StageText", 4);
        Func<Task> execute = () => Call<CommandResult<int>>(parent, "Replace", Request());
        await execute.Should().ThrowAsync<OverflowException>();
        ParentStore(data).Find(1)!.Revision.Should().Be(3);
        ChildStore(data).Find(2).Should().NotBeNull();
        ChildStore(data).Find(20).Should().BeNull();
        data.PendingCount.Should().Be(1);
    }

    [Fact]
    public async Task PreCanceledCollectionWriteDoesNotAlterMappedSavedOrPendingRows()
    {
        var (parent, child, data) = Fixture();
        await Call<CommandResult>(child, "StageText", 4);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> execute = async () => await (Task<CommandResult<int>>)parent.GetType().GetMethod("ReplaceAsync")!
            .Invoke(parent, [Request(), cancellation.Token])!;
        await execute.Should().ThrowAsync<OperationCanceledException>();
        ChildStore(data).Find(1)!.Revision.Should().Be(3);
        ParentStore(data).Find(1)!.Revision.Should().Be(3);
        data.PendingCount.Should().Be(1);
    }

    [Fact]
    public async Task ExplicitScriptsStillOverrideEligibleMappedCollectionWrites()
    {
        var (parent, _, data) = Fixture();
        ScriptedTestDoubleTestHarness.Returns(parent, "OnReplaceAsync", new CommandResult<int>(CommandStatus.NotMatched, value: 42));
        (await Call<CommandResult<int>>(parent, "Replace", Request())).Value.Should().Be(42);
        data.PendingCount.Should().Be(0);
        ParentStore(data).Find(1)!.Title.Should().Be("old");
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("bound")]
    [InlineData("collision")]
    public async Task InputAndIdentityFailuresNeverLeavePartialMappedChanges(string invalid)
    {
        var (parent, _, data) = Fixture();
        var request = Request();
        request = request with { Items = invalid switch
        {
            "duplicate" => [request.Items[0], request.Items[0]],
            "bound" => Enumerable.Range(20, 5).Select(id => new MappedCollectionItem(id, "code" + id, "new")).ToArray(),
            _ => [request.Items[0], new(4, "c", "colliding identity")]
        } };
        var before = JsonSerializer.Serialize(ChildStore(data).Snapshot());
        Func<Task> execute = () => Call<CommandResult<int>>(parent, "Replace", request);
        await execute.Should().ThrowAsync<Exception>();
        JsonSerializer.Serialize(ChildStore(data).Snapshot()).Should().Be(before);
        ParentStore(data).Find(1)!.Revision.Should().Be(3);
        data.PendingCount.Should().Be(0);
    }
}
