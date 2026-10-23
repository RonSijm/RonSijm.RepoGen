using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryManyToManyTests
{
    [Fact]
    public void CompositeStructuralKeysDifferentEndpointAritiesAndJoinOrderAreRespected()
    {
        FixtureRow[] owners = [new() { Id = 1, Name = "north" }, new() { Id = 2, Name = "south" }, new() { Id = 3, Name = "empty" }];
        FixtureRow[] targets = [new() { Id = 10 }, new() { Id = 20 }];
        FixtureRow[] joins = [new() { Id = 20, Name = "north" }, new() { Id = 10, Name = "north" }, new() { Id = 20, Name = "south" }];
        var found = new Dictionary<int, List<FixtureRow>>();
        InMemoryRelationships.LoadManyToMany("tags", owners, targets, joins, row => [new byte[] { 1, 2 }, row.Name], row => [row.Id],
            row => [new byte[] { 1, 2 }, row.Name], row => [row.Id], (row, related) => found.Add(row.Id, related));
        found[1].Should().Equal(targets[1], targets[0]); found[2].Should().Equal(targets[1]); found[3].Should().BeEmpty();
        found[1][0].Should().BeSameAs(found[2][0], "identity is shared only within this detached snapshot");
        found[1].Clear(); found[2].Should().HaveCount(1);
    }

    [Fact]
    public void OptionalNullAndUnmatchedJoinKeysNeverMatchOrInventTargets()
    {
        FixtureRow[] rows = [new() { Id = 1, Name = "One" }];
        FixtureRow[] joins = [new() { Id = 1, Name = "null" }, new() { Id = 2, Name = "One" }, new() { Id = 1, Name = "one" }];
        List<FixtureRow>? found = null;
        InMemoryRelationships.LoadManyToMany("optional", rows, rows, joins, row => [row.Name], row => [row.Id],
            row => [row.Name == "null" ? null : row.Name], row => [row.Id], (_, related) => found = related,
            requiredSource: false, requiredTarget: false);
        found.Should().BeEmpty("null, unmatched targets and case differences do not join");
    }

    [Theory]
    [InlineData("owner", "owner relationship keys")]
    [InlineData("target", "target relationship keys")]
    [InlineData("pair", "join relationship pairs")]
    [InlineData("missingOwner", "required join reference*owner")]
    [InlineData("missingTarget", "required join reference*target")]
    [InlineData("nullOwner", "required join reference*owner")]
    [InlineData("nullTarget", "required join reference*target")]
    public void InvalidFixtureRelationshipsNeverPartiallyAssignAnEdge(string scenario, string message)
    {
        FixtureRow[] rows = [new() { Id = 1 }, new() { Id = 2 }];
        FixtureRow[] joins = scenario == "pair" ? [new() { Id = 1 }, new() { Id = 1 }] : rows;
        var assignments = 0;
        Action load = () => InMemoryRelationships.LoadManyToMany("invalid", rows, rows, joins,
            row => [scenario == "owner" ? 1 : row.Id], row => [scenario == "target" ? 1 : row.Id],
            row => [scenario switch { "missingOwner" => 99, "nullOwner" => (object?)null, _ => row.Id }],
            row => [scenario switch { "missingTarget" => 99, "nullTarget" => (object?)null, _ => row.Id }], (_, _) => assignments++);
        load.Should().Throw<InvalidOperationException>().WithMessage("*invalid*" + message + "*"); assignments.Should().Be(0);
    }

    [Theory]
    [InlineData("sourceArity")]
    [InlineData("targetArity")]
    [InlineData("empty")]
    [InlineData("null")]
    public void BothSidesValidateKeyShapesIndependently(string scenario)
    {
        FixtureRow[] rows = [new() { Id = 1 }];
        Action load = () => InMemoryRelationships.LoadManyToMany("keys", rows, rows, rows, row => [row.Id], row => [row.Id],
            row => scenario switch { "sourceArity" => [row.Id, row.Name], "empty" => [], "null" => null!, _ => [row.Id] },
            row => scenario == "targetArity" ? [row.Id, row.Name] : [row.Id], (_, _) => { });
        load.Should().Throw<InvalidOperationException>().WithMessage("*keys*key selectors*");
    }

    [Theory]
    [InlineData("before")]
    [InlineData("owner")]
    [InlineData("target")]
    [InlineData("join")]
    [InlineData("assignment")]
    public void CancellationIsObservedAtEveryPhaseIncludingTheLastCallback(string phase)
    {
        using var cancellation = new CancellationTokenSource();
        if (phase == "before") cancellation.Cancel();
        FixtureRow[] rows = [new() { Id = 1 }]; var assignments = 0;
        object?[] Key(FixtureRow row, string at) { if (phase == at) cancellation.Cancel(); return [row.Id]; }
        Action load = () => InMemoryRelationships.LoadManyToMany("cancel", rows, rows, rows, row => Key(row, "owner"), row => Key(row, "target"),
            row => Key(row, "join"), row => [row.Id], (_, _) => { assignments++; if (phase == "assignment") cancellation.Cancel(); }, cancellationToken: cancellation.Token);
        load.Should().Throw<OperationCanceledException>(); assignments.Should().Be(phase == "assignment" ? 1 : 0);
    }

    [Fact]
    public void LastTargetCancellationIsObservedWithNoOwnersOrJoins()
    {
        using var cancellation = new CancellationTokenSource();
        FixtureRow[] rows = [new() { Id = 1 }];
        object?[] Key(FixtureRow row) { cancellation.Cancel(); return [row.Id]; }
        Action load = () => InMemoryRelationships.LoadManyToMany<FixtureRow, FixtureRow, FixtureRow>("last", [], rows, [],
            row => [row.Id], Key, row => [row.Id], row => [row.Id], (_, _) => { }, cancellationToken: cancellation.Token);
        load.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void CancellationInTheJoinSourceGetterDoesNotCallTheTargetGetter()
    {
        using var cancellation = new CancellationTokenSource(); FixtureRow[] rows = [new() { Id = 1 }]; var targetCalls = 0;
        object?[] Source(FixtureRow row) { cancellation.Cancel(); return [row.Id]; }
        object?[] Target(FixtureRow row) { targetCalls++; return [row.Id]; }
        Action load = () => InMemoryRelationships.LoadManyToMany("stop", rows, rows, rows, row => [row.Id], row => [row.Id], Source, Target, (_, _) => { }, cancellationToken: cancellation.Token);
        load.Should().Throw<OperationCanceledException>(); targetCalls.Should().Be(0);
    }

    [Fact]
    public void SelfEdgesUseTheSameDetachedRowsWithoutImplicitInverseAssignments()
    {
        FixtureRow[] rows = [new() { Id = 1 }, new() { Id = 2 }, new() { Id = 3 }];
        FixtureRow[] joins = [new() { Id = 1, Name = "2" }, new() { Id = 2, Name = "3" }]; var found = new Dictionary<int, List<FixtureRow>>();
        InMemoryRelationships.LoadManyToMany("self", rows, rows, joins, row => [row.Id], row => [row.Id], row => [row.Id],
            row => [int.Parse(row.Name, System.Globalization.CultureInfo.InvariantCulture)], (row, related) => found.Add(row.Id, related));
        found[1].Single().Should().BeSameAs(rows[1]); found[2].Single().Should().BeSameAs(rows[2]); found[3].Should().BeEmpty();
    }

    [Fact]
    public void JoinGetterFailureNeverAssignsAndEmptyStoresRemainEmpty()
    {
        FixtureRow[] rows = [new() { Id = 1 }, new() { Id = 2 }]; var assignments = 0;
        Action load = () => InMemoryRelationships.LoadManyToMany("getter", rows, rows, rows, row => [row.Id], row => [row.Id],
            row => row.Id == 2 ? throw new InvalidOperationException("getter failed") : [row.Id], row => [row.Id], (_, _) => assignments++);
        load.Should().Throw<InvalidOperationException>().WithMessage("getter failed");
        InMemoryRelationships.LoadManyToMany<FixtureRow, FixtureRow, FixtureRow>("empty", [], [], [], row => [row.Id], row => [row.Id], row => [row.Id], row => [row.Id], (_, _) => assignments++);
        assignments.Should().Be(0);
        Action missing = () => InMemoryRelationships.LoadManyToMany<FixtureRow, FixtureRow, FixtureRow>("args", [], [], null!, row => [row.Id], row => [row.Id], row => [row.Id], row => [row.Id], (_, _) => { });
        missing.Should().Throw<ArgumentNullException>();
    }
}
