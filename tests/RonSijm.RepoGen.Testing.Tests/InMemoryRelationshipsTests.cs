using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryRelationshipsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationOnTheLastTargetIsObservedEvenWithNoSourceRows(bool reference)
    {
        using var cancellation = new CancellationTokenSource();
        FixtureRow[] targets = [new() { Id = 1 }];
        object?[] Key(FixtureRow row) { cancellation.Cancel(); return [row.Id]; }
        Action load = reference
            ? () => InMemoryRelationships.LoadReference<FixtureRow, FixtureRow>("last", [], targets, row => [row.Id], Key, (_, _) => { }, cancellationToken: cancellation.Token)
            : () => InMemoryRelationships.LoadCollection<FixtureRow, FixtureRow>("last", [], targets, row => [row.Id], Key, (_, _) => { }, cancellationToken: cancellation.Token);
        load.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void CompositeNullableOrdinalAndStructuralKeysHaveExplicitJoinSemantics()
    {
        FixtureRow[] sources = [new() { Id = 1, Name = "north" }, new() { Id = 2, Name = "North" }, new() { Id = 3, Name = "null" }];
        FixtureRow[] targets = [new() { Id = 10, Name = "north" }, new() { Id = 11, Name = "south" }];
        var found = new Dictionary<int, FixtureRow?>();
        InMemoryRelationships.LoadReference("ref", sources, targets,
            row => [new byte[] { 1, 2 }, row.Name == "null" ? null : row.Name], row => [new byte[] { 1, 2 }, row.Name],
            (row, related) => found[row.Id] = related);
        found[1].Should().BeSameAs(targets[0]);
        found[2].Should().BeNull("case differences are not a database collation");
        found[3].Should().BeNull("a null key component never joins");
    }

    [Fact]
    public void CollectionLookupPreservesTargetOrderAndDoesNotReuseMutableLists()
    {
        FixtureRow[] sources = [new() { Id = 1 }, new() { Id = 2 }, new() { Id = 3 }];
        FixtureRow[] targets = [new() { Id = 4, Name = "1" }, new() { Id = 5, Name = "1" }, new() { Id = 6, Name = "2" }, new() { Id = 7, Name = "null" }];
        var found = new Dictionary<int, List<FixtureRow>>();
        InMemoryRelationships.LoadCollection("collection", sources, targets, row => [row.Id], row => [row.Name == "null" ? null : int.Parse(row.Name, System.Globalization.CultureInfo.InvariantCulture)],
            (row, related) => found[row.Id] = related);
        found[1].Should().Equal(targets[0], targets[1]);
        found[2].Should().Equal(targets[2]);
        found[3].Should().BeEmpty();
        found[1].Clear();
        targets.Should().HaveCount(4);
        found[2].Should().HaveCount(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuplicateUniqueSideNeverPartiallyAssignsARelationship(bool reference)
    {
        FixtureRow[] rows = [new() { Id = 1 }, new() { Id = 2 }];
        var assignments = 0;
        Action load = reference
            ? () => InMemoryRelationships.LoadReference("edge", rows, rows, row => [row.Id], row => [1], (_, _) => assignments++)
            : () => InMemoryRelationships.LoadCollection("edge", rows, rows, row => [1], row => [row.Id], (_, _) => assignments++);
        load.Should().Throw<InvalidOperationException>().WithMessage("*edge*keys are not unique*");
        assignments.Should().Be(0);
    }

    [Fact]
    public void NullCompositeTargetKeysDoNotConflictOrJoin()
    {
        FixtureRow[] rows = [new() { Id = 1 }, new() { Id = 2 }];
        var results = new List<FixtureRow?>();
        InMemoryRelationships.LoadReference("nullable", rows, rows, row => [row.Id, null], row => [row.Id, null], (_, related) => results.Add(related));
        results.Should().OnlyContain(row => row == null);
    }

    [Fact]
    public void RequiredReferenceAndFailingGetterValidateAllInputsBeforeAssignment()
    {
        FixtureRow[] rows = [new() { Id = 1 }, new() { Id = 2 }];
        var assignments = 0;
        Action missing = () => InMemoryRelationships.LoadReference("required", rows, [rows[0]], row => [row.Id], row => [row.Id], (_, _) => assignments++, required: true);
        missing.Should().Throw<InvalidOperationException>().WithMessage("*required reference*no matching target*");
        Action failing = () => InMemoryRelationships.LoadCollection("getter", rows, rows,
            row => row.Id == 2 ? throw new InvalidOperationException("getter failed") : [row.Id], row => [row.Id], (_, _) => assignments++);
        failing.Should().Throw<InvalidOperationException>().WithMessage("getter failed");
        assignments.Should().Be(0);
    }

    [Theory]
    [InlineData("Empty")]
    [InlineData("Null")]
    [InlineData("Arity")]
    public void InvalidKeySelectorsAreClearErrorsRatherThanUnmatchedRows(string kind)
    {
        FixtureRow[] rows = [new() { Id = 1 }];
        Action load = () => InMemoryRelationships.LoadReference("keys", rows, rows,
            row => kind switch { "Empty" => [], "Null" => null!, _ => [row.Id, row.Name] }, row => [row.Id], (_, _) => { });
        load.Should().Throw<InvalidOperationException>().WithMessage("*keys*key selectors*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationBeforeOrDuringPreparationDoesNotAssignOrInvokeUnnecessarySelectors(bool preCanceled)
    {
        using var cancellation = new CancellationTokenSource();
        if (preCanceled) cancellation.Cancel();
        FixtureRow[] rows = [new() { Id = 1 }, new() { Id = 2 }];
        var selectors = 0;
        var assignments = 0;
        Action load = () => InMemoryRelationships.LoadReference("cancellation", rows, rows, row => [row.Id], row =>
        {
            selectors++;
            cancellation.Cancel();
            return [row.Id];
        }, (_, _) => assignments++, cancellationToken: cancellation.Token);
        load.Should().Throw<OperationCanceledException>();
        selectors.Should().Be(preCanceled ? 0 : 1);
        assignments.Should().Be(0);
    }

    [Fact]
    public void LoadingOnlyMutatesDetachedSnapshotRowsAndNeverCurrentStoresOrBaselines()
    {
        var context = new InMemoryDataContext();
        var sources = context.CreateStore<FixtureRow, int>("sources", row => row.Id, FixtureRow.Copy);
        var targets = context.CreateStore<FixtureRow, int>("targets", row => row.Id, FixtureRow.Copy);
        sources.Seed(new() { Id = 1, Name = "original" });
        targets.Seed(new() { Id = 1, Name = "target" });
        context.CaptureBaseline();
        var snapshot = context.CaptureSnapshot("sources", "targets");
        InMemoryRelationships.LoadReference("snapshot", snapshot.GetRows<FixtureRow>("sources"), snapshot.GetRows<FixtureRow>("targets"),
            row => [row.Id], row => [row.Id], (row, related) => row.Name = related!.Name);
        snapshot.GetRows<FixtureRow>("sources").Single().Name.Should().Be("target");
        sources.Find(1)!.Name.Should().Be("original");
        context.RestoreBaseline();
        sources.Find(1)!.Name.Should().Be("original");
    }

    [Fact]
    public void EmptyStoresProduceNoCallbacksAndArgumentsAreValidated()
    {
        var assignments = 0;
        InMemoryRelationships.LoadReference<FixtureRow, FixtureRow>("empty", [], [], row => [row.Id], row => [row.Id], (_, _) => assignments++);
        InMemoryRelationships.LoadCollection<FixtureRow, FixtureRow>("empty", [], [], row => [row.Id], row => [row.Id], (_, _) => assignments++);
        assignments.Should().Be(0);
        Action invalid = () => InMemoryRelationships.LoadReference<FixtureRow, FixtureRow>(" ", [], [], row => [row.Id], row => [row.Id], (_, _) => { });
        invalid.Should().Throw<ArgumentException>();
        Action missing = () => InMemoryRelationships.LoadCollection<FixtureRow, FixtureRow>("empty", [], [], null!, row => [row.Id], (_, _) => { });
        missing.Should().Throw<ArgumentNullException>();
    }
}
