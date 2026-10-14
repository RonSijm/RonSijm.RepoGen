using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryNullTargetTests
{
    [Fact]
    public void NullAndUnmatchedTargetsRetainOneSlotPerJoinWithoutInventingMissingOwners()
    {
        FixtureRow[] owners = [new() { Id = 1, Name = "one" }, new() { Id = 2, Name = "empty" }];
        FixtureRow[] targets = [new() { Id = 10 }];
        FixtureRow[] joins = [new() { Id = 10, Name = "one" }, new() { Id = 0, Name = "one" }, new() { Id = 0, Name = "one" },
            new() { Id = 99, Name = "one" }, new() { Id = 10, Name = "missing" }, new() { Id = 10, Name = "null" }];
        var related = new Dictionary<int, List<FixtureRow>>();
        InMemoryRelationships.LoadManyToMany("nulls", owners, targets, joins, row => [row.Name], row => [row.Id],
            row => [row.Name == "null" ? null : row.Name], row => [row.Id == 0 ? null : row.Id], (owner, rows) => related.Add(owner.Id, rows),
            requiredSource: false, requiredTarget: false, preserveMissingTargets: true);
        related[1].Should().Equal(targets[0], null!, null!, null!); related[2].Should().BeEmpty();
    }

    [Theory]
    [InlineData("required")]
    [InlineData("owner")]
    [InlineData("pair")]
    [InlineData("targetKeys")]
    [InlineData("arity")]
    public void PreservationCannotBypassRelationshipValidityOrPartiallyAssign(string invalid)
    {
        FixtureRow[] rows = [new() { Id = 1 }, new() { Id = 2 }]; var assigned = 0;
        FixtureRow[] joins = invalid == "pair" ? [new() { Id = 1 }, new() { Id = 1 }] : rows;
        Action load = () => InMemoryRelationships.LoadManyToMany("validate", rows, rows, joins, row => [row.Id], row => [invalid == "targetKeys" ? 1 : row.Id],
            row => [invalid == "owner" ? 99 : row.Id], row => invalid == "arity" ? [row.Id, row.Name] : [row.Id], (_, _) => assigned++,
            requiredSource: true, requiredTarget: invalid == "required", preserveMissingTargets: true);
        if (invalid == "required") load.Should().Throw<ArgumentException>().WithParameterName("preserveMissingTargets");
        else load.Should().Throw<InvalidOperationException>();
        assigned.Should().Be(0);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("assignment")]
    public void CancellationIsRetainedEvenForNullOnlyTargets(string phase)
    {
        using var cancellation = new CancellationTokenSource(); if (phase == "before") cancellation.Cancel();
        FixtureRow[] owners = [new() { Id = 1 }]; var assigned = 0;
        object?[] Source(FixtureRow row) { if (phase == "source") cancellation.Cancel(); return [row.Id]; }
        object?[] Target(FixtureRow row) { if (phase == "target") cancellation.Cancel(); return [null]; }
        Action load = () => InMemoryRelationships.LoadManyToMany("cancel", owners, Array.Empty<FixtureRow>(), owners, row => [row.Id], row => [row.Id], Source, Target,
            (_, _) => { assigned++; if (phase == "assignment") cancellation.Cancel(); }, requiredSource: true, requiredTarget: false,
            preserveMissingTargets: true, cancellationToken: cancellation.Token);
        load.Should().Throw<OperationCanceledException>(); assigned.Should().Be(phase == "assignment" ? 1 : 0);
    }
}
