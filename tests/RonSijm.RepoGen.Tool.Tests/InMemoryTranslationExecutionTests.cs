using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryTranslationExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactCallsAreTranslatedAcrossAllReadClausesWithoutExecutingProductionHelpers(bool mapped)
    {
        var repository = InMemoryTranslationTestHarness.Repository(mapped);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "FindAsync", "%AP%")).Should().Equal(1, 2);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "NestedAsync", "%AP%")).Should().Equal(1, 2);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "DefaultAsync")).Should().Equal(1, 2, 3);
        (await Invoke<Task<IReadOnlyList<TranslationGroup>>>(repository, "GroupedAsync")).Should().Equal(new TranslationGroup(4, 21), new TranslationGroup(5, 42));
        (await Invoke<Task<IReadOnlyList<PortableView>>>(repository, "PipelineAsync", "%a%", 1)).Select(row => row.Id).Should().Equal(1);
        (await Invoke<Task<int?>>(repository, "FirstValueAsync", 1)).Should().Be(11);
        (await Invoke<Task<int?>>(repository, "FirstValueAsync", 99)).Should().BeNull();
        (await Invoke<Task<int?>>(repository, "AnnotatedValueAsync", 99)).Should().BeNull();
        (await Invoke<Task<IReadOnlyDictionary<int, int>>>(repository, "DictionaryAsync"))[3].Should().Be(4);
        (await Invoke<Task<int>>(repository, "SumAsync")).Should().Be(63);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "FromAsync")).Should().Equal(11, 21, 31);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "MembershipAsync", new PortableCriteria(Ids: [2, 4]))).Should().Equal(1, 3);
        var page = await Invoke<Task<PagedResult<PortableView>>>(repository, "FieldSortAsync", new PortableCriteria(), 1, 2);
        page.Items.Select(row => row.Id).Should().Equal(3, 1); page.TotalCount.Should().Be(3);
        var cursor = await Invoke<Task<CursorPagedResult<int, int>>>(repository, "CursorAsync", new CursorPageRequest<int>(2));
        cursor.Items.Should().Equal(11, 31); cursor.NextCursor.Should().Be(2); cursor.HasMore.Should().BeTrue();
        (await Invoke<Task<int>>(repository, "CountAsync")).Should().Be(3);
        Func<Task> unmatched = () => Invoke<Task<IReadOnlyList<int>>>(repository, "WrongFixedAsync");
        await unmatched.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamsRemainColdCancelableAndScriptedOverridesStillWin(bool mapped)
    {
        var repository = InMemoryTranslationTestHarness.Repository(mapped);
        var stream = Invoke<IAsyncEnumerable<int>>(repository, "StreamAsync", "%a%");
        var values = new List<int>(); await foreach (var value in stream) values.Add(value);
        values.Should().Equal(1, 2, 3);
        ScriptedTestDoubleTestHarness.Returns(repository, "OnFindAsync", new List<int> { 99 });
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "FindAsync", "never")).Should().Equal(99);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Func<Task> iterate = async () => { await foreach (var _ in stream.WithCancellation(canceled.Token)) { } };
        await iterate.Should().ThrowAsync<OperationCanceledException>();
        Func<Task> invalid = () => Invoke<Task<IReadOnlyList<PortableView>>>(repository, "PipelineAsync", "", 1);
        await invalid.Should().ThrowAsync<ArgumentException>();
    }
}
