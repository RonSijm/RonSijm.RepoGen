using System.Globalization;
using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryFilteredManyToManyTests
{
    [Fact]
    public void FilteredRequiredTargetsOmitLinksAndFilteredOwnersAreNotAssigned()
    {
        FixtureRow[] owners = [new() { Id = 1 }, new() { Id = 2 }];
        FixtureRow[] targets = [new() { Id = 10 }, new() { Id = 20 }];
        FixtureRow[] joins = [new() { Id = 1, Name = "20" }, new() { Id = 2, Name = "10" }];
        var found = new Dictionary<int, List<FixtureRow>>();
        Load([owners[0]], [targets[0]], joins, owners, targets, (row, related) => found.Add(row.Id, related));
        found.Should().ContainSingle(); found[1].Should().BeEmpty();
        owners.Should().HaveCount(2); targets.Should().HaveCount(2); joins.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalFilteredTargetsFollowTheExistingExplicitNullSlotPolicy(bool preserve)
    {
        FixtureRow[] owners = [new() { Id = 1 }]; FixtureRow[] targets = [new() { Id = 10 }];
        FixtureRow[] joins = [new() { Id = 1, Name = "10" }]; List<FixtureRow>? found = null;
        Load(owners, [], joins, owners, targets, (_, related) => found = related, requiredTarget: false, preserve: preserve);
        if (preserve) found.Should().Equal((FixtureRow)null!); else found.Should().BeEmpty();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("target")]
    [InlineData("duplicateOwner")]
    [InlineData("duplicateTarget")]
    [InlineData("duplicatePair")]
    [InlineData("visibleOwner")]
    [InlineData("visibleTarget")]
    [InlineData("arity")]
    public void FilteringDoesNotConcealInvalidPrincipalKeysOrVisibleJoinPairs(string mode)
    {
        FixtureRow[] owners = [new() { Id = 1 }]; FixtureRow[] targets = [new() { Id = 10 }];
        FixtureRow[] joins = [new() { Id = mode == "owner" ? 2 : 1, Name = mode == "target" ? "20" : "10" }];
        var assignments = 0;
        Action load = () => Load(mode == "visibleOwner" ? [new() { Id = 2 }] : [],
            mode == "visibleTarget" ? [new() { Id = 20 }] : [],
            mode == "duplicatePair" ? [joins[0], joins[0]] : joins,
            mode == "duplicateOwner" ? [owners[0], owners[0]] : owners,
            mode == "duplicateTarget" ? [targets[0], targets[0]] : targets,
            (_, _) => assignments++, joinSource: mode == "arity" ? row => [row.Id, row.Name] : null);
        load.Should().Throw<InvalidOperationException>(); assignments.Should().Be(0);
    }

    [Fact]
    public void ExplicitComponentComparisonAppliesToHiddenPrincipalKeysAndPairValidation()
    {
        FixtureRow[] owners = [new() { Id = 1 }]; FixtureRow[] targets = [new() { Id = 10, Name = "UPPER" }];
        FixtureRow[] joins = [new() { Id = 1, Name = "upper" }];
        var comparer = new InMemoryRelationshipKeyComparer(InMemoryRelationshipKeyComparer.Component<string>(StringComparer.OrdinalIgnoreCase));
        List<FixtureRow>? found = null;
        InMemoryRelationships.LoadFilteredManyToMany("compared", owners, Array.Empty<FixtureRow>(), joins, owners, targets,
            row => [row.Id], row => [row.Name], row => [row.Id], row => [row.Name], (_, related) => found = related,
            targetKeyComparer: comparer);
        found.Should().BeEmpty();
    }

    [Theory]
    [InlineData("before")]
    [InlineData("unfilteredOwner")]
    [InlineData("unfilteredTarget")]
    [InlineData("join")]
    [InlineData("assign")]
    public void CancellationAndGetterFailuresNeverMutateFixtureData(string phase)
    {
        using var cancellation = new CancellationTokenSource();
        FixtureRow[] rows = [new() { Id = 1, Name = "1" }]; var assignments = 0;
        if (phase == "before") cancellation.Cancel();
        object?[] Key(FixtureRow row, string at) { if (phase == at) cancellation.Cancel(); return [row.Id]; }
        Action load = () => InMemoryRelationships.LoadFilteredManyToMany("cancel", rows, rows, rows, rows, rows,
            row => Key(row, "unfilteredOwner"), row => Key(row, "unfilteredTarget"), row => Key(row, "join"), row => [row.Id],
            (_, _) => { assignments++; if (phase == "assign") cancellation.Cancel(); }, cancellationToken: cancellation.Token);
        load.Should().Throw<OperationCanceledException>(); assignments.Should().Be(phase == "assign" ? 1 : 0);
        rows[0].Name.Should().Be("1");
    }

    private static void Load(IReadOnlyList<FixtureRow> owners, IReadOnlyList<FixtureRow> targets, IReadOnlyList<FixtureRow> joins,
        IReadOnlyList<FixtureRow> allOwners, IReadOnlyList<FixtureRow> allTargets, Action<FixtureRow, List<FixtureRow>> assign,
        bool requiredTarget = true, bool preserve = false, Func<FixtureRow, object?[]>? joinSource = null)
        => InMemoryRelationships.LoadFilteredManyToMany("filtered", owners, targets, joins, allOwners, allTargets,
            row => [row.Id], row => [row.Id], joinSource ?? (row => [row.Id]), row => [int.Parse(row.Name, CultureInfo.InvariantCulture)],
            assign, requiredTarget: requiredTarget, preserveMissingTargets: preserve);
}
