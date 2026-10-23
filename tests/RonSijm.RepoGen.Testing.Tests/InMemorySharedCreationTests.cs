using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemorySharedCreationTests
{
    [Theory]
    [InlineData("reject")]
    [InlineData("throw")]
    [InlineData("cancel")]
    [InlineData("createCancel")]
    public async Task SharedCreationValidationFailsBeforeVersionsBindingOrGeneratedValuesAndRetainsEarlierWork(string failure)
    {
        var (data, parents, children) = Setup(); using var cancellation = new CancellationTokenSource();
        var earlier = data.CreateStore<InMemoryWriteRow, int>("earlier", row => row.Id, InMemoryWriteRow.Copy);
        var calls = new List<string>();
        data.ConfigureGeneratedValues(children, (row, rows) => { calls.Add("generated"); row.Id = 10; });
        await data.CreateAsync(earlier, () => new() { Id = 9, Name = "earlier" }, row => row.Id, save: false);
        var plan = Plan(data, children, calls, failure == "createCancel" ? item =>
        {
            calls.Add("create"); cancellation.Cancel(); return new() { Key = item.Key, OwnerId = 1 };
        } : null, (parent, child) =>
        {
            calls.Add("guard");
            if (failure == "throw") throw new InvalidOperationException("reference guard");
            if (failure == "cancel") cancellation.Cancel();
            return failure != "reject";
        });
        Func<Task> run = () => Run(data, parents, [plan], cancellation.Token);
        if (failure == "reject") await run.Should().ThrowAsync<ArgumentException>().WithMessage("*selected owner*");
        else if (failure == "throw") await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("reference guard");
        else await run.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Equal(failure == "createCancel" ? ["create"] : ["create", "guard"]);
        parents.Find(1)!.Name.Should().Be("parent"); children.Snapshot().Should().ContainSingle().Which.Text.Should().Be("old");
        data.PendingCount.Should().Be(1); (await data.SaveChangesAsync()).Should().Be(1); earlier.Find(9).Should().NotBeNull();
    }

    [Fact]
    public async Task SuccessfulAddValidatesItsInitializerOnceBeforeOwnerBindingAndVersionChecks()
    {
        var (data, parents, children) = Setup(); var calls = new List<string>();
        data.ConfigureGeneratedValues(children, (row, rows) => { calls.Add("generated"); row.Id = rows.Max(item => item.Id) + 1; });
        var plan = Plan(data, children, calls, null, (parent, child) => { calls.Add("guard"); child.OwnerId.Should().Be(parent.Id); return true; });
        (await Run(data, parents, [plan])).AffectedRows.Should().Be(3);
        calls.Should().Equal("create", "guard", "version", "update", "bind", "generated");
        children.Find(2)!.OwnerId.Should().Be(1); children.Find(1)!.Text.Should().Be("updated");
    }

    [Fact]
    public async Task LaterSharedReferenceFailureRestoresAnEarlierCollectionPlan()
    {
        var (data, parents, children) = Setup(); var calls = new List<string>();
        data.ConfigureGeneratedValues(children, (row, rows) => row.Id = rows.Max(item => item.Id) + 1);
        var second = data.CreateStore<InMemoryCollectionRow, int>("second", row => row.Id, InMemoryCollectionRow.Copy);
        var firstPlan = Plan(data, children, calls, null, static (_, _) => true);
        var secondPlan = Plan(data, second, calls, null, static (_, _) => false);
        Func<Task> fail = () => Run(data, parents, [firstPlan, secondPlan]);
        await fail.Should().ThrowAsync<ArgumentException>().WithMessage("*selected owner*");
        data.PendingCount.Should().Be(0); parents.Find(1)!.Name.Should().Be("parent"); children.Snapshot().Should().ContainSingle().Which.Text.Should().Be("old");
    }

    private static (InMemoryWriteDataContext Data, InMemoryStore<InMemoryWriteRow, int> Parents, InMemoryStore<InMemoryCollectionRow, int> Children) Setup()
    {
        var data = new InMemoryWriteDataContext();
        var parents = data.CreateStore<InMemoryWriteRow, int>("parents", row => row.Id, InMemoryWriteRow.Copy);
        var children = data.CreateStore<InMemoryCollectionRow, int>("children", row => row.Id, InMemoryCollectionRow.Copy);
        parents.Seed(new() { Id = 1, Name = "parent", Version = 1 }); children.Seed(new() { Id = 1, OwnerId = 1, Key = "a", Text = "old" });
        return (data, parents, children);
    }

    private static InMemoryCollectionWrite<InMemoryWriteRow> Plan(InMemoryWriteDataContext data, InMemoryStore<InMemoryCollectionRow, int> children,
        List<string> calls, Func<InMemoryCollectionItem, InMemoryCollectionRow>? create, Func<InMemoryWriteRow, InMemoryCollectionRow, bool> guard)
        => data.ReconcileCollection<InMemoryWriteRow, InMemoryCollectionRow, InMemoryCollectionItem, int, string>(children,
            RepositoryBatchInputs.Prepare<InMemoryCollectionItem, string>([new("a", "updated"), new("b", "new")], item => item.Key, 5),
            (parent, child) => child.OwnerId == parent.Id, static _ => true, child => child.Key,
            create ?? (item => { calls.Add("create"); return new() { OwnerId = 1, Key = item.Key, Text = item.Text }; }),
            (parent, child) => { calls.Add("bind"); child.OwnerId = parent.Id; },
            (child, item) => { calls.Add("update"); child.Text = item.Text; return [nameof(child.Text)]; },
            (child, item) => { calls.Add("version"); return true; },
            (left, right) => left.Text == right.Text, (left, right) => left.Version == right.Version,
            (target, source, assigned) => target.Text = source.Text, 5, deleteMissing: false, rejectMissing: false, matchesCreatedOwner: guard);

    private static Task<CommandResult<string>> Run(InMemoryWriteDataContext data, InMemoryStore<InMemoryWriteRow, int> parents,
        InMemoryCollectionWrite<InMemoryWriteRow>[] plans, CancellationToken cancellationToken = default)
        => data.ReconcileAsync(parents, row => row.Id == 1, row => { row.Name = "changed"; return [nameof(row.Name)]; }, plans, static _ => true,
            (left, right) => left.Name == right.Name, (left, right) => left.Version == right.Version,
            (target, source, assigned) => target.Name = source.Name, row => row.Name, save: true, cancellationToken: cancellationToken);
}
