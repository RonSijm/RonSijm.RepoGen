using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryStoreTests
{
    [Fact]
    public void SeedFindAndSnapshotDetachRootAndNestedMutableData()
    {
        var store = Store();
        var input = new FixtureRow { Id = 1, Name = "seed", Tags = ["one"] };
        store.Seed(input);
        input.Name = "changed";
        input.Tags.Add("two");
        var found = store.Find(1)!;
        found.Name.Should().Be("seed");
        found.Tags.Should().Equal("one");
        found.Tags.Clear();
        var snapshot = store.Snapshot();
        snapshot[0].Id = 99;
        snapshot[0].Tags.Add("three");
        store.Find(1)!.Tags.Should().Equal("one");
        store.Find(99).Should().BeNull();
        store.Find(1).Should().NotBeSameAs(found);
        store.Count.Should().Be(1);
        Action alterCollection = () => ((IList<FixtureRow>)snapshot).Clear();
        alterCollection.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateFailuresNeverApplyPartialRows(bool existingKey)
    {
        var store = Store();
        store.Seed(new() { Id = 1, Name = "original" });
        Action seed = () => store.SeedRange([new() { Id = 2 }, new() { Id = existingKey ? 1 : 2 }]);
        seed.Should().Throw<InvalidOperationException>().WithMessage("Duplicate key*");
        store.Snapshot().Should().ContainSingle().Which.Name.Should().Be("original");
    }

    [Theory]
    [InlineData(InMemoryDuplicateKeyBehavior.Ignore, "old", "first")]
    [InlineData(InMemoryDuplicateKeyBehavior.Replace, "new", "last")]
    public void DuplicatePoliciesApplyToExistingAndRepeatedInputKeys(InMemoryDuplicateKeyBehavior policy, string first, string second)
    {
        var store = Store();
        store.Seed(new() { Id = 1, Name = "old" });
        store.SeedRange([new() { Id = 1, Name = "new" }, new() { Id = 2, Name = "first" }, new() { Id = 2, Name = "last" }], policy);
        store.Count.Should().Be(2);
        store.Find(1)!.Name.Should().Be(first);
        store.Find(2)!.Name.Should().Be(second);
    }

    [Fact]
    public void ReplacementIsAtomicAndDoesNotChangeBaseline()
    {
        var context = new InMemoryDataContext();
        var store = context.CreateStore<FixtureRow, int>("rows", row => row.Id, FixtureRow.Copy);
        store.Seed(new() { Id = 1, Name = "baseline" });
        context.CaptureBaseline();
        Action failedReplace = () => store.ReplaceWith([new() { Id = 2 }, new() { Id = 2 }]);
        failedReplace.Should().Throw<InvalidOperationException>();
        store.Find(1)!.Name.Should().Be("baseline");
        store.ReplaceWith([new() { Id = 3, Name = "replacement" }]);
        store.Find(1).Should().BeNull();
        store.Find(3)!.Name.Should().Be("replacement");
        context.RestoreBaseline();
        store.Snapshot().Should().ContainSingle().Which.Name.Should().Be("baseline");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidCopyAdaptersFailWithoutMutatingCurrentRows(int mode)
    {
        var invalid = false;
        var context = new InMemoryDataContext();
        var store = context.CreateStore<FixtureRow, int>("rows", row => row.Id, row => !invalid ? FixtureRow.Copy(row) : mode switch
        {
            0 => null!,
            1 => row,
            _ => new FixtureRow { Id = row.Id + 1 }
        });
        store.Seed(new() { Id = 1 });
        invalid = true;
        Action seed = () => store.Seed(new() { Id = 2 });
        seed.Should().Throw<InvalidOperationException>().WithMessage("The fixture copy adapter*");
        store.Count.Should().Be(1);
        invalid = false;
        store.Find(2).Should().BeNull();
    }

    [Fact]
    public void NullKeysAndNullRowsAreRejected()
    {
        var store = new InMemoryDataContext().CreateStore<FixtureRow, string>("rows", row => row.Name, FixtureRow.Copy);
        Action nullKey = () => store.Seed(new() { Name = null! });
        nullKey.Should().Throw<InvalidOperationException>().WithMessage("*key adapter returned null*");
        Action nullCopyKey = () => new InMemoryDataContext().CreateStore<FixtureRow, string>("rows", row => row.Name,
            row => new() { Id = row.Id, Name = null! }).Seed(new() { Name = "valid" });
        nullCopyKey.Should().Throw<InvalidOperationException>().WithMessage("*changed the row key*");
        Action nullRow = () => store.SeedRange([null!]);
        nullRow.Should().Throw<ArgumentNullException>();
        Action nullFind = () => store.Find(null!);
        nullFind.Should().Throw<ArgumentNullException>();
        Action nullRemove = () => store.Remove(null!);
        nullRemove.Should().Throw<ArgumentNullException>();
        store.Count.Should().Be(0);
    }

    [Fact]
    public void FailingCopyOrInputEnumerationLeavesTheEntireRangeUnapplied()
    {
        var store = new InMemoryDataContext().CreateStore<FixtureRow, int>("rows", row => row.Id,
            row => row.Id == 3 ? throw new InvalidOperationException("copy failure") : FixtureRow.Copy(row));
        store.Seed(new() { Id = 1 });
        Action copyFailure = () => store.SeedRange([new() { Id = 2 }, new() { Id = 3 }]);
        copyFailure.Should().Throw<InvalidOperationException>().WithMessage("copy failure");
        Action enumerationFailure = () => store.SeedRange(FailingRows());
        enumerationFailure.Should().Throw<InvalidOperationException>().WithMessage("enumeration failure");
        store.Snapshot().Select(row => row.Id).Should().Equal(1);

        static IEnumerable<FixtureRow> FailingRows()
        {
            yield return new() { Id = 2 };
            throw new InvalidOperationException("enumeration failure");
        }
    }

    [Fact]
    public void InputsAreEnumeratedOnceAndInvalidPoliciesAreRejectedBeforeEnumeration()
    {
        var enumerations = 0;
        var store = Store();
        store.SeedRange(Rows());
        enumerations.Should().Be(1);
        Action badPolicy = () => store.SeedRange(Rows(), (InMemoryDuplicateKeyBehavior)999);
        badPolicy.Should().Throw<ArgumentOutOfRangeException>();
        enumerations.Should().Be(1);
        store.Count.Should().Be(1);

        IEnumerable<FixtureRow> Rows()
        {
            enumerations++;
            yield return new() { Id = 1 };
        }
    }

    [Fact]
    public void CustomImmutableKeyEqualityIsHonoredForSeedsLookupsAndRemovals()
    {
        var store = new InMemoryDataContext().CreateStore<FixtureRow, string>("rows", row => row.Name, FixtureRow.Copy, StringComparer.OrdinalIgnoreCase);
        store.Seed(new() { Name = "one" });
        store.Seed(new() { Name = "ONE", Id = 2 }, InMemoryDuplicateKeyBehavior.Replace);
        store.Count.Should().Be(1);
        store.Find("One")!.Id.Should().Be(2);
        store.Remove("oNe").Should().BeTrue();
        store.Remove("absent").Should().BeFalse();
        store.Count.Should().Be(0);
    }

    [Fact]
    public void InspectionsRejectAStoredKeyCorruptedByAnIncorrectShallowCopyAdapter()
    {
        var context = new InMemoryDataContext();
        // Deliberately violate the documented nested-copy contract, to test the key invariant guard.
        var store = context.CreateStore<FixtureRow, string>("rows", row => row.Tags[0],
            row => new() { Id = row.Id, Tags = row.Tags });
        var seed = new FixtureRow { Id = 1, Tags = ["one"] };
        store.Seed(seed);
        seed.Tags[0] = "two";
        Action find = () => store.Find("one");
        Action inspect = () => store.Snapshot();
        Action capture = context.CaptureBaseline;
        find.Should().Throw<InvalidOperationException>().WithMessage("*key changed after arrangement*");
        inspect.Should().Throw<InvalidOperationException>().WithMessage("*key changed after arrangement*");
        capture.Should().Throw<InvalidOperationException>().WithMessage("*key changed after arrangement*");
        store.Count.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentArrangementDoesNotLoseRows()
    {
        var store = Store();
        await Task.WhenAll(Enumerable.Range(1, 50).Select(id => Task.Run(() => store.Seed(new() { Id = id }))));
        store.Count.Should().Be(50);
        store.Snapshot().Select(row => row.Id).Should().BeEquivalentTo(Enumerable.Range(1, 50));
    }

    private static InMemoryStore<FixtureRow, int> Store() => new InMemoryDataContext().CreateStore<FixtureRow, int>("rows", row => row.Id, FixtureRow.Copy);
}
