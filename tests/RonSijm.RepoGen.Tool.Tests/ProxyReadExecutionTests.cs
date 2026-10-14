using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ProxyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

[Collection("Proxy reads")]
public sealed class ProxyReadExecutionTests
{
    [Fact]
    public async Task ScalarMappingsApplyBeforeVisibilitySortingProjectionAndAllTerminals()
    {
        var repository = Repository();
        (await Invoke<Task<IReadOnlyList<ProxyReadView>>>(repository, "AllAsync")).Select(row => row.Name).Should().Equal("APPLE", "PAPER", "PEAR");
        (await Invoke<Task<ProxyReadView?>>(repository, "FindAsync", 1)).Should().Be(new ProxyReadView(1, "APPLE", "fruit", 20, null));
        (await Invoke<Task<ProxyReadView?>>(repository, "FindAsync", 4)).Should().BeNull();
        (await Invoke<Task<int>>(repository, "CountAsync")).Should().Be(3);
        (await Invoke<Task<bool>>(repository, "AnyAsync", 61)).Should().BeFalse();
        (await Invoke<Task<IReadOnlyList<string>>>(repository, "NamesAsync")).Should().Equal("APPLE", "PAPER", "PEAR");
        (await Invoke<Task<IReadOnlyList<ProxyReadView>>>(repository, "SearchAsync", "PA")).Should().ContainSingle().Which.Id.Should().Be(2);
        (await Invoke<Task<IReadOnlyList<ProxyReadView>>>(repository, "DynamicAsync", "score", true)).Select(row => row.Id).Should().Equal(2, 3, 1);
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(repository, "DictionaryAsync")).Should().Contain(1, "APPLE");
        (await Invoke<Task<int>>(repository, "SumAsync")).Should().Be(120);
        (await Invoke<Task<IReadOnlyList<ProxyReadTotal>>>(repository, "ReportAsync")).Should().Equal(new ProxyReadTotal("fruit", 2, 60), new ProxyReadTotal("supplies", 1, 60));
        var page = await Invoke<Task<PagedResult<ProxyReadView>>>(repository, "PageAsync", 2, 2);
        page.TotalCount.Should().Be(3);
        page.Items.Should().ContainSingle().Which.Id.Should().Be(3);
        var cursor = await Invoke<Task<CursorPagedResult<ProxyReadView, int>>>(repository, "CursorAsync", new CursorPageRequest<int>(2));
        cursor.Items.Select(row => row.Id).Should().Equal(1, 2);
        cursor.HasMore.Should().BeTrue();
        (await Invoke<Task<CursorPagedResult<ProxyReadView, int>>>(repository, "CursorAsync", new CursorPageRequest<int>(cursor.NextCursor, 2)))
            .Items.Should().ContainSingle().Which.Id.Should().Be(3);
    }

    [Fact]
    public async Task MultipleMappedPrivateRootsJoinWithVisibilityAndNullSafeLeftRows()
    {
        var repository = Repository();
        (await Invoke<Task<IReadOnlyList<ProxyReadView>>>(repository, "JoinAsync")).Should().ContainSingle().Which.Department.Should().Be("Office");
        var left = await Invoke<Task<IReadOnlyList<ProxyReadView>>>(repository, "LeftAsync");
        left.Select(row => row.Id).Should().Equal(1, 2, 3);
        left.Select(row => row.Department).Should().Equal("Office", null, null);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "UnmatchedAsync")).Should().Equal(2, 3);
        (await Invoke<Task<IReadOnlyList<string>>>(repository, "UnionAsync")).Should().Equal("APPLE", "Office", "PAPER", "PEAR");
    }

    [Fact]
    public async Task ReadMappingNeverConstructsOriginalEntitiesAndMutableResultsAreDetached()
    {
        using var context = Context();
        var before = ProxyReadEntity.Constructions;
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new ProxyReadRepositoryDesigner()));
        var repository = Repository();
        var seed = new ProxyReadData { Key = 10, Caption = "copy", Stamp = [10] };
        Store(repository).Seed(seed);
        seed.Stamp[0] = 99;
        var first = await Invoke<Task<IReadOnlyList<byte[]>>>(repository, "BytesAsync");
        first[^1][0].Should().Be(10);
        first[^1][0] = 88;
        (await Invoke<Task<IReadOnlyList<byte[]>>>(repository, "BytesAsync"))[^1][0].Should().Be(10);
        ProxyReadEntity.Constructions.Should().Be(before);
    }

    [Fact]
    public async Task MissingFieldsStillThrowForEmptyStoresAndTypedScriptsOverrideWithoutChangingData()
    {
        var repository = Repository();
        Store(repository).Clear();
        Func<Task> missing = () => Invoke<Task<IReadOnlyList<int>>>(repository, "MissingAsync");
        await missing.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        ScriptedTestDoubleTestHarness.Returns(repository, "OnMissingAsync", new List<int> { 42 });
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "MissingAsync")).Should().Equal(42);
        repository.GetType().GetMethod("ResetTestDouble")!.Invoke(repository, null);
        await missing.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        Store(repository).Count.Should().Be(0);
    }

    [Fact]
    public async Task ColdStreamMapsOneDetachedSnapshotAndHonorsCancellation()
    {
        var repository = Repository();
        var stream = Invoke<IAsyncEnumerable<ProxyReadView>>(repository, "StreamAsync");
        Store(repository).Seed(new() { Key = 1, Caption = "changed", RawScore = 2 }, InMemoryDuplicateKeyBehavior.Replace);
        await using var iterator = stream.GetAsyncEnumerator();
        (await iterator.MoveNextAsync()).Should().BeTrue();
        iterator.Current.Name.Should().Be("CHANGED");
        Store(repository).Clear();
        var count = 1;
        while (await iterator.MoveNextAsync()) count++;
        count.Should().Be(3);
        await using var fresh = stream.GetAsyncEnumerator();
        (await fresh.MoveNextAsync()).Should().BeFalse();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await using var canceled = stream.GetAsyncEnumerator(cancellation.Token);
        Func<Task> read = async () => await canceled.MoveNextAsync();
        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task MappedSourcesUseOneContextPartitionAndKeepBaselinesAndScriptsIndependent()
    {
        using var context = Context();
        const string consumer = """
            using System.Threading.Tasks;
            using Generated.Proxies.TestDoubles;
            public static class MappedPartitions
            {
                public static async Task<bool> Exercise()
                {
                    var fixture = new ProxyReadContextRepositoryFixture();
                    fixture.For("one").ProxyReadEntityStore.Seed(new() { Key = 1, Caption = "first", DepartmentKey = 1 });
                    fixture.For("two").ProxyReadDepartmentStore.Seed(new(1, "second"));
                    var repository = new ProxyReadEntityRepositoryTestDouble(fixture);
                    if ((await repository.JoinAsync("one")).Count != 0) return false;
                    fixture.For("one").ProxyReadDepartmentStore.Seed(new(1, "own"));
                    fixture.For("one").CaptureBaseline();
                    if ((await repository.JoinAsync("one"))[0].Department != "own") return false;
                    fixture.For("one").Clear();
                    fixture.For("one").RestoreBaseline();
                    repository.ResetTestDouble();
                    return (await repository.FindAsync(1, "one"))?.Name == "FIRST"
                        && (await repository.CountAsync("two")) == 0;
                }
            }
            """;
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new ProxyReadRepositoryDesigner(keyed: true)), consumer);
        (await (Task<bool>)assembly.GetType("MappedPartitions")!.GetMethod("Exercise")!.Invoke(null, null)!).Should().BeTrue();
    }
}
