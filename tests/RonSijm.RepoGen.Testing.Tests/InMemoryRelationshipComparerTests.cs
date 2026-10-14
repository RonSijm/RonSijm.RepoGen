using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryRelationshipComparerTests
{
    private static InMemoryRelationshipKeyComparer Comparison() => new(null, InMemoryRelationshipKeyComparer.Component<string>(StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void TypedComponentsUseConsistentEqualityHashingAndStructuralFallbacks()
    {
        InMemoryRelationshipKeyComparer.ComponentComparer?[] components = [null, InMemoryRelationshipKeyComparer.Component<string>(StringComparer.OrdinalIgnoreCase)];
        var comparer = new InMemoryRelationshipKeyComparer(components); components[1] = null;
        object?[] left = [new byte[] { 1, 2 }, "mixed"]; object?[] right = [new byte[] { 1, 2 }, "MIXED"];
        comparer.Equals(left, right).Should().BeTrue(); comparer.GetHashCode(left).Should().Be(comparer.GetHashCode(right));
        comparer.Equals(left, [new byte[] { 1, 3 }, "MIXED"]).Should().BeFalse();
        comparer.Equals([1, null], [1, null]).Should().BeTrue(); comparer.Equals(null, null).Should().BeTrue(); comparer.Equals(left, null).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceAndCollectionKeysMatchOnlyTheConfiguredComponent(bool reference)
    {
        FixtureRow[] sources = [new() { Id = 1, Name = "A" }, new() { Id = 2, Name = "A" }];
        FixtureRow[] targets = [new() { Id = 1, Name = "a" }, new() { Id = 2, Name = "a" }];
        var results = new List<FixtureRow?>();
        if (reference) InMemoryRelationships.LoadReference("typed", sources, targets, row => [row.Id, row.Name], row => [row.Id, row.Name], (_, row) => results.Add(row), Comparison(), required: true);
        else InMemoryRelationships.LoadCollection("typed", sources, targets, row => [row.Id, row.Name], row => [row.Id, row.Name], (_, rows) => results.Add(rows.Single()), Comparison());
        results.Should().Equal(targets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EquivalentUniqueSideFailsBeforeAnyAssignment(bool reference)
    {
        FixtureRow[] rows = [new() { Id = 1, Name = "a" }, new() { Id = 2, Name = "A" }]; var assignments = 0;
        Action load = reference
            ? () => InMemoryRelationships.LoadReference("unique", rows, rows, row => [1, row.Name], row => [1, row.Name], (_, _) => assignments++, Comparison())
            : () => InMemoryRelationships.LoadCollection("unique", rows, rows, row => [1, row.Name], row => [1, row.Name], (_, _) => assignments++, Comparison());
        load.Should().Throw<InvalidOperationException>().WithMessage("*keys are not unique*"); assignments.Should().Be(0);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("target")]
    [InlineData("pair")]
    public void BothManyToManyComparersGovernTheirOwnUniquenessContracts(string duplicate)
    {
        FixtureRow[] sources = duplicate == "owner" ? [new() { Id = 1, Name = "a" }, new() { Id = 2, Name = "A" }] : [new() { Id = 1, Name = "a" }];
        FixtureRow[] targets = duplicate == "target" ? [new() { Id = 1, Name = "b" }, new() { Id = 2, Name = "B" }] : [new() { Id = 1, Name = "b" }];
        FixtureRow[] joins = duplicate == "pair" ? [new() { Id = 1, Name = "AB" }, new() { Id = 2, Name = "ab" }] : [new() { Id = 1, Name = "AB" }];
        var assignments = 0;
        Action load = () => InMemoryRelationships.LoadManyToMany("two", sources, targets, joins, row => [1, row.Name], row => [1, row.Name],
            row => [1, row.Name[..1]], row => [1, row.Name[1..]], (_, _) => assignments++, Comparison(), Comparison());
        load.Should().Throw<InvalidOperationException>().WithMessage("*not unique*"); assignments.Should().Be(0);
    }

    [Fact]
    public void ComparedNullableTargetsRetainNullSlotsWithoutMatchingThem()
    {
        FixtureRow[] sources = [new() { Id = 1, Name = "a" }]; FixtureRow[] targets = [new() { Id = 1, Name = "b" }];
        FixtureRow[] joins = [new() { Id = 1, Name = "B" }, new() { Id = 2, Name = "" }, new() { Id = 3, Name = "" }];
        var results = new List<FixtureRow>();
        InMemoryRelationships.LoadManyToMany("nullable", sources, targets, joins, row => [1, row.Name], row => [1, row.Name],
            _ => [1, "A"], row => [1, row.Name.Length == 0 ? null : row.Name], (_, rows) => results = rows, Comparison(), Comparison(),
            requiredTarget: false, preserveMissingTargets: true);
        results.Should().Equal(targets[0], null!, null!);
    }

    [Theory]
    [InlineData("arity")]
    [InlineData("type")]
    [InlineData("nullArity")]
    public void MalformedComparedKeysFailBeforeRelationshipAssignment(string scenario)
    {
        FixtureRow[] rows = [new() { Id = 1 }]; var assignments = 0;
        object?[] Key(FixtureRow _) => scenario switch { "arity" => [1], "nullArity" => [null], _ => [1, 2] };
        Action load = () => InMemoryRelationships.LoadCollection("shape", rows, rows, Key, Key, (_, _) => assignments++, Comparison());
        load.Should().Throw<ArgumentException>(); assignments.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComparerFailureOrCancellationDoesNotAssignTheEdge(bool cancel)
    {
        using var cancellation = new CancellationTokenSource(); var assignments = 0;
        var comparer = new InMemoryRelationshipKeyComparer(InMemoryRelationshipKeyComparer.Component(new InterruptingComparer(cancellation, cancel)));
        FixtureRow[] rows = [new() { Id = 1, Name = "one" }];
        Action load = () => InMemoryRelationships.LoadReference("interrupt", rows, rows, row => [row.Name], row => [row.Name],
            (_, _) => assignments++, comparer, cancellationToken: cancellation.Token);
        if (cancel) load.Should().Throw<OperationCanceledException>(); else load.Should().Throw<InvalidOperationException>().WithMessage("comparison failed");
        assignments.Should().Be(0);
    }

    private sealed class InterruptingComparer(CancellationTokenSource cancellation, bool cancel) : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => StringComparer.Ordinal.Equals(x, y);
        public int GetHashCode(string obj)
        {
            if (!cancel) throw new InvalidOperationException("comparison failed");
            cancellation.Cancel(); return StringComparer.Ordinal.GetHashCode(obj);
        }
    }

    [Fact]
    public void HashCollisionsStillUseTypedEqualityToSelectTheCorrectTarget()
    {
        FixtureRow[] targets = [new() { Id = 1, Name = "one" }, new() { Id = 2, Name = "two" }];
        FixtureRow[] sources = [new() { Id = 3, Name = "TWO" }]; FixtureRow? result = null;
        var comparer = new InMemoryRelationshipKeyComparer(InMemoryRelationshipKeyComparer.Component<string>(new ConstantHashComparer()));
        InMemoryRelationships.LoadReference("collision", sources, targets, row => [row.Name], row => [row.Name], (_, related) => result = related, comparer, required: true);
        result.Should().BeSameAs(targets[1]);
    }

    [Fact]
    public void NullKeysNeverReachTheConfiguredComparerOrJoinEachOther()
    {
        using var cancellation = new CancellationTokenSource(); FixtureRow[] rows = [new() { Id = 1 }]; FixtureRow? result = rows[0];
        var comparer = new InMemoryRelationshipKeyComparer(InMemoryRelationshipKeyComparer.Component<string>(new InterruptingComparer(cancellation, false)));
        InMemoryRelationships.LoadReference("null", rows, rows, _ => [null], _ => [null], (_, related) => result = related, comparer);
        result.Should().BeNull();
    }

    private sealed class ConstantHashComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => StringComparer.OrdinalIgnoreCase.Equals(x, y);
        public int GetHashCode(string obj) => 0;
    }
}
