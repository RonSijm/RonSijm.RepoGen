using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryOptionalJoinTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NullOrUnmatchedOptionalKeysDoNotJoinButValidRowsRetainSnapshotOrder(bool requiredSource, bool requiredTarget)
    {
        FixtureRow[] rows = [new() { Id = 1 }, new() { Id = 2 }];
        FixtureRow[] joins = [new() { Id = 1, Name = "null" }, new() { Id = 2, Name = "null" }, new() { Id = 2, Name = "valid" }, new() { Id = 1, Name = "valid" }];
        var found = new Dictionary<int, List<FixtureRow>>();
        object?[] From(FixtureRow row) => [row.Name == "null" && !requiredSource ? null : 1];
        object?[] To(FixtureRow row) => [row.Name == "null" && !requiredTarget ? null : row.Id];
        InMemoryRelationships.LoadManyToMany("nullable", rows, rows, joins, row => [row.Id], row => [row.Id], From, To,
            (row, related) => found.Add(row.Id, related), requiredSource, requiredTarget);
        found[1].Should().Equal(rows[1], rows[0]); found[2].Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheOtherRequiredSideIsStillValidatedWhenAnOptionalSideIsNull(bool requiredSource)
    {
        FixtureRow[] rows = [new() { Id = 1 }]; var assigned = false;
        Action load = () => InMemoryRelationships.LoadManyToMany("required", rows, rows, rows, row => [row.Id], row => [row.Id],
            _ => [requiredSource ? 99 : null], _ => [requiredSource ? null : 99], (_, _) => assigned = true,
            requiredSource: requiredSource, requiredTarget: !requiredSource);
        load.Should().Throw<InvalidOperationException>().WithMessage(requiredSource ? "*required join reference*owner*" : "*required join reference*target*");
        assigned.Should().BeFalse();
    }

    [Fact]
    public void PartiallyNullCompositeReferencesDoNotMatchAndDoNotCreateDuplicateCompletePairs()
    {
        FixtureRow[] rows = [new() { Id = 1, Name = "code" }];
        FixtureRow[] joins = [new() { Id = 1, Name = "null" }, new() { Id = 1, Name = "null" }, new() { Id = 1, Name = "code" }];
        List<FixtureRow>? found = null;
        InMemoryRelationships.LoadManyToMany("partial", rows, rows, joins, row => [row.Id, row.Name], row => [row.Id],
            row => [row.Id, row.Name == "null" ? null : row.Name], row => [row.Id], (_, related) => found = related,
            requiredSource: false);
        found.Should().Equal(rows);
    }

    [Fact]
    public void OptionalityNeverSuppressesShapeValidationOrGetterCancellation()
    {
        FixtureRow[] rows = [new() { Id = 1 }]; var assigned = false;
        Action shape = () => InMemoryRelationships.LoadManyToMany("shape", rows, rows, rows, row => [row.Id], row => [row.Id],
            _ => [null, null], _ => [null], (_, _) => assigned = true, requiredSource: false, requiredTarget: false);
        shape.Should().Throw<InvalidOperationException>().WithMessage("*different component counts*"); assigned.Should().BeFalse();
        using var cancellation = new CancellationTokenSource(); var targets = 0;
        object?[] Source(FixtureRow _) { cancellation.Cancel(); return [null]; }
        object?[] Target(FixtureRow _) { targets++; return [null]; }
        Action cancel = () => InMemoryRelationships.LoadManyToMany("cancel", rows, rows, rows, row => [row.Id], row => [row.Id],
            Source, Target, (_, _) => assigned = true, false, false, cancellation.Token);
        cancel.Should().Throw<OperationCanceledException>(); assigned.Should().BeFalse(); targets.Should().Be(0);
    }
}
