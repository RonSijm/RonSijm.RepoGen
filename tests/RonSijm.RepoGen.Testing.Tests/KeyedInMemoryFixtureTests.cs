using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class KeyedInMemoryFixtureTests
{
    [Fact]
    public void EqualKeysShareStoresAndDifferentKeysHaveIndependentBaselines()
    {
        var fixture = new KeyedInMemoryFixture<string, PartitionData>();
        var first = fixture.For("first");
        first.Rows.Seed(new() { Id = 1, Name = "first" });
        first.CaptureBaseline();
        var second = fixture.For("second");
        second.Rows.Seed(new() { Id = 1, Name = "second" });
        fixture.For(new string("first".ToCharArray())).Should().BeSameAs(first);
        second.Should().NotBeSameAs(first);
        first.Clear();
        first.RestoreBaseline();
        first.Rows.Find(1)!.Name.Should().Be("first");
        second.Rows.Find(1)!.Name.Should().Be("second");
        second.RestoreBaseline();
        second.Rows.Count.Should().Be(0);
        first.Rows.Count.Should().Be(1);
        fixture.Keys.Should().BeEquivalentTo("first", "second");
    }

    [Fact]
    public async Task ConcurrentPartitionSelectionCreatesOneSharedInstance()
    {
        var fixture = new KeyedInMemoryFixture<int, PartitionData>();
        var partitions = await Task.WhenAll(Enumerable.Range(1, 50).Select(_ => Task.Run(() => fixture.For(1))));
        partitions.Should().OnlyContain(data => ReferenceEquals(data, partitions[0]));
        fixture.Keys.Should().Equal(1);
    }

    [Fact]
    public void CustomKeyEqualityAndDetachedKeyInventoryAreExplicit()
    {
        var fixture = new KeyedInMemoryFixture<string, PartitionData>(StringComparer.OrdinalIgnoreCase);
        fixture.For("one").Should().BeSameAs(fixture.For("ONE"));
        var keys = fixture.Keys;
        fixture.For("two");
        keys.Should().Equal("one");
        Action edit = () => ((IList<string>)keys).Clear();
        edit.Should().Throw<NotSupportedException>();
        Action nullKey = () => fixture.For(null!);
        nullKey.Should().Throw<ArgumentNullException>();
    }

    public sealed class PartitionData : InMemoryDataContext
    {
        public InMemoryStore<FixtureRow, int> Rows { get; }
        public PartitionData() => Rows = CreateStore<FixtureRow, int>("rows", row => row.Id, FixtureRow.Copy);
    }
}
