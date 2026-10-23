using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MappedKeyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedKeyExecutionTests
{
    [Fact]
    public async Task CompositeMappedKeysLoadReferencesCollectionsAndBothJoinDirectionsWithoutGetters()
    {
        var (owner, child, tag, data) = Fixture();
        MappedKeyOwner.GetterCalls.Value = 0;
        var cards = await Invoke<Task<IReadOnlyList<MappedKeyCard>>>(owner, "CardsAsync");
        cards.Select(row => row.Id).Should().Equal(1, 2);
        cards[0].Children.Should().Equal("first");
        cards[0].Tags.Should().Equal("blue");
        cards[1].Children.Should().Equal("second");
        cards[1].Tags.Should().Equal("red");
        var children = await Invoke<Task<IReadOnlyList<MappedKeyChildCard>>>(child, "CardsAsync");
        children.Select(row => row.Owner).Should().Equal("one", "two", "none", "none");
        var inverse = await Invoke<Task<IReadOnlyList<List<string>>>>(tag, "OwnersAsync");
        inverse[0].Should().Equal("two", "private"); // Related repository visibility is not injected.
        inverse[1].Should().Equal("one");
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "SourceAsync")).Should().Equal(1, 1);
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "HasTagAsync", "blue")).Should().Equal(1);
        var page = await Invoke<Task<PagedResult<MappedKeyCard>>>(owner, "PageAsync", 2, 1);
        page.TotalCount.Should().Be(2);
        page.Items.Single().Id.Should().Be(2);
        (await Invoke<Task<IReadOnlyList<MappedKeyCard>>>(owner, "AllAsync")).Select(row => row.Id).Should().Equal(1, 2, 3);
        MappedKeyOwner.GetterCalls.Value.Should().Be(0);
        Store<MappedKeyOwnerSeed>(data, nameof(MappedKeyOwner)).Count.Should().Be(4);
    }

    [Fact]
    public async Task DetachedResultsColdStreamsAndBaselinesRetainSnapshotIsolation()
    {
        var (owner, _, _, data) = Fixture();
        data.CaptureBaseline();
        (await Invoke<Task<IReadOnlyList<MappedKeyCard>>>(owner, "CardsAsync"))[0].Tags.Clear();
        var stream = Invoke<IAsyncEnumerable<MappedKeyCard>>(owner, "StreamAsync");
        await using var rows = stream.GetAsyncEnumerator();
        (await rows.MoveNextAsync()).Should().BeTrue();
        rows.Current.Tags.Should().Equal("blue");
        Store<MappedKeyLinkSeed>(data, nameof(MappedKeyLink)).Seed(new(2, 8, "a", 2), InMemoryDuplicateKeyBehavior.Replace);
        (await rows.MoveNextAsync()).Should().BeTrue();
        rows.Current.Tags.Should().Equal("red");
        (await Invoke<Task<IReadOnlyList<MappedKeyCard>>>(owner, "CardsAsync"))[1].Tags.Should().Equal("blue");
        data.RestoreBaseline();
        (await Invoke<Task<IReadOnlyList<MappedKeyCard>>>(owner, "CardsAsync"))[1].Tags.Should().Equal("red");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteRelationshipKeysAreUniqueEvenWhenStoreIdsDiffer(bool join)
    {
        var (owner, _, _, data) = Fixture();
        if (join)
        {
            Store<MappedKeyLinkSeed>(data, nameof(MappedKeyLink)).Seed(new(99, 7, "a", 2));
        }
        else
        {
            Store<MappedKeyOwnerSeed>(data, nameof(MappedKeyOwner)).Seed(new(99, 7, "a", "duplicate"));
        }
        Func<Task> read = async () => await Invoke<Task<IReadOnlyList<MappedKeyCard>>>(owner, "CardsAsync");
        await read.Should().ThrowAsync<InvalidOperationException>();
        Store<MappedKeyOwnerSeed>(data, nameof(MappedKeyOwner)).Find(1)!.Name.Should().Be("one");
        Store<MappedKeyOwnerSeed>(data, nameof(MappedKeyOwner)).Count.Should().Be(join ? 4 : 5);
    }

    [Fact]
    public async Task NamedKeyRecipesDoNotRedefineFixtureDictionaryIdentity()
    {
        using var context = MappedKeyTestHarness.Context();
        var builder = MappedKeyTestHarness.Builder();
        builder.Entity<MappedKeyTag>().MapInMemoryProperty<MappedKeyTagSeed, int>(nameof(MappedKeyTag.Id), row => row.Id + 100);
        var (owner, _, _, data) = Fixture(ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build())));
        (await Invoke<Task<IReadOnlyList<MappedKeyCard>>>(owner, "CardsAsync"))[0].Tags.Should().BeEmpty();
        Store<MappedKeyLinkSeed>(data, nameof(MappedKeyLink)).Seed(new(1, 7, "a", 102), InMemoryDuplicateKeyBehavior.Replace);
        (await Invoke<Task<IReadOnlyList<MappedKeyCard>>>(owner, "CardsAsync"))[0].Tags.Should().Equal("blue");
        Store<MappedKeyTagSeed>(data, nameof(MappedKeyTag)).Find(2)!.Id.Should().Be(2);
        Store<MappedKeyTagSeed>(data, nameof(MappedKeyTag)).Find(102).Should().BeNull();
    }
}
