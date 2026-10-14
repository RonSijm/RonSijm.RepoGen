using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryQueryExecutionTests
{
    [Fact]
    public async Task NamedProjectionCardinalityVisibilityDefaultSortAndBoundNamesAreReused()
    {
        var repository = Repository();
        (await ((IPortableReadPort)repository).FindAsync(1)).Should().Be(new PortableView(1, "apple", "fruit", 10));
        (await ((IPortableReadPort)repository).FindAsync(99)).Should().BeNull();
        (await Invoke<Task<int>>(repository, "CountAsync")).Should().Be(3);
        (await Invoke<Task<bool>>(repository, "AnyAsync", 31)).Should().BeFalse();
        (await Invoke<Task<IReadOnlyList<PortableView>>>(repository, "DefaultAsync")).Select(row => row.Id).Should().Equal(3, 2, 1);
        (await Invoke<Task<IReadOnlyList<PortableView>>>(repository, "AllIncludingHiddenAsync")).Should().HaveCount(4);
        (await Invoke<Task<PortableView?>>(repository, "LatestAsync"))!.Id.Should().Be(2);
        Func<Task> required = () => Invoke<Task<PortableView>>(repository, "RequireAsync", 99);
        Func<Task> duplicate = () => Invoke<Task<PortableView?>>(repository, "RequireOneAsync");
        await required.Should().ThrowAsync<InvalidOperationException>();
        await duplicate.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("FirstValueAsync")]
    [InlineData("SimpleValueAsync")]
    [InlineData("AnnotatedValueAsync")]
    public async Task MissingValueProjectionsReturnNullRatherThanDefaultZero(string method)
    {
        var repository = Repository();
        (await Invoke<Task<int?>>(repository, method, 99)).Should().BeNull();
        (await Invoke<Task<int?>>(repository, method, 1)).Should().Be(10);
    }

    [Fact]
    public async Task CriteriaFragmentsMembershipResultFieldSortAndConditionalLimitsExecute()
    {
        var repository = Repository();
        var criteria = new PortableCriteria(Term: "ap", Ids: [1, 2, 2], SortBy: "PRICE", Descending: true);
        (await Invoke<Task<IReadOnlyList<PortableView>>>(repository, "SearchAsync", criteria)).Select(row => row.Id).Should().Equal(2, 1);
        (await Invoke<Task<IReadOnlyList<PortableView>>>(repository, "SearchAsync", criteria with { Limited = true, Limit = 1 })).Should().ContainSingle().Which.Id.Should().Be(2);
        (await Invoke<Task<IReadOnlyList<PortableView>>>(repository, "SearchAsync", new PortableCriteria(Ids: []))).Should().BeEmpty();
        // Disabled count expressions must not evaluate/validate negative values.
        (await Invoke<Task<IReadOnlyList<PortableView>>>(repository, "SearchAsync", new PortableCriteria(Limit: -1))).Should().HaveCount(3);
        Func<Task> negative = () => Invoke<Task<IReadOnlyList<PortableView>>>(repository, "SearchAsync", new PortableCriteria(Limited: true, Limit: -1));
        Func<Task> invalid = () => Invoke<Task<IReadOnlyList<PortableView>>>(repository, "SearchAsync", new PortableCriteria(Minimum: -1));
        Func<Task> invalidSort = () => Invoke<Task<IReadOnlyList<PortableView>>>(repository, "SearchAsync", new PortableCriteria(SortBy: "Name.Length"));
        Func<Task> tooMany = () => Invoke<Task<IReadOnlyList<PortableView>>>(repository, "SearchAsync", new PortableCriteria(Ids: Enumerable.Repeat(1, 11)));
        await negative.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await invalid.Should().ThrowAsync<ArgumentException>();
        await invalidSort.Should().ThrowAsync<ArgumentException>();
        await tooMany.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task DynamicEntitySortUsesOnlyWhitelistedNames()
    {
        var repository = Repository();
        (await Invoke<Task<IReadOnlyList<PortableView>>>(repository, "DynamicAsync", "pRiCe", true)).Select(row => row.Id).Should().Equal(2, 3, 1);
        Func<Task> invalid = () => Invoke<Task<IReadOnlyList<PortableView>>>(repository, "DynamicAsync", "Price.ToString()", true);
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task CountedPagesAndUncountedSlicesHonorBoundariesAndOverflowChecks()
    {
        var repository = Repository();
        var page = await Invoke<Task<PagedResult<PortableView>>>(repository, "PageAsync", 2, 2);
        page.Items.Should().ContainSingle().Which.Id.Should().Be(3);
        page.TotalCount.Should().Be(3);
        page.Page.Should().Be(2);
        var slice = await Invoke<Task<SliceResult<PortableView>>>(repository, "SliceAsync", 1, 2);
        slice.Items.Select(row => row.Id).Should().Equal(1, 2);
        slice.HasMore.Should().BeTrue();
        (await Invoke<Task<SliceResult<PortableView>>>(repository, "SliceAsync", 2, 2)).HasMore.Should().BeFalse();
        (await Invoke<Task<PagedResult<PortableView>>>(repository, "PageAsync", 9, 2)).Items.Should().BeEmpty();
        Func<Task> invalid = () => Invoke<Task<PagedResult<PortableView>>>(repository, "PageAsync", 0, 2);
        Func<Task> overflow = () => Invoke<Task<PagedResult<PortableView>>>(repository, "PageAsync", int.MaxValue, 2);
        Func<Task> sliceOverflow = () => Invoke<Task<SliceResult<PortableView>>>(repository, "SliceAsync", 1, int.MaxValue);
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await overflow.Should().ThrowAsync<OverflowException>();
        await sliceOverflow.Should().ThrowAsync<OverflowException>();
    }

    [Theory]
    [InlineData("CursorAsync")]
    [InlineData("ResultCursorAsync")]
    public async Task EntityAndProjectedCursorsUseCorrectRowShape(string method)
    {
        var repository = Repository();
        var first = await Invoke<Task<CursorPagedResult<PortableView, int>>>(repository, method, new CursorPageRequest<int>(2));
        first.Items.Select(row => row.Id).Should().Equal(1, 2);
        first.NextCursor.Should().Be(2);
        first.HasMore.Should().BeTrue();
        var second = await Invoke<Task<CursorPagedResult<PortableView, int>>>(repository, method, new CursorPageRequest<int>(first.NextCursor, 2));
        second.Items.Should().ContainSingle().Which.Id.Should().Be(3);
        second.HasMore.Should().BeFalse();
        var empty = await Invoke<Task<CursorPagedResult<PortableView, int>>>(repository, method, new CursorPageRequest<int>(99, 2));
        empty.Items.Should().BeEmpty();
        empty.NextCursor.Should().Be(0);
    }

    [Fact]
    public async Task GroupedReportsAnonymousIntermediatesDistinctAndScalarsArePortable()
    {
        var repository = Repository();
        var expected = new[] { new PortableReport("fruit", 2, 30, 20), new PortableReport("supplies", 1, 30, 30) };
        (await Invoke<Task<IReadOnlyList<PortableReport>>>(repository, "ReportAsync")).Should().Equal(expected);
        (await Invoke<Task<IReadOnlyList<PortableReport>>>(repository, "ResultGroupsAsync")).Should().Equal(expected);
        (await Invoke<Task<IReadOnlyList<string>>>(repository, "CategoriesAsync")).Should().Equal("fruit", "supplies");
        (await Invoke<Task<int>>(repository, "SumAsync")).Should().Be(60);
        (await Invoke<Task<double>>(repository, "AverageAsync")).Should().Be(20);
        (await Invoke<Task<int>>(repository, "MaxAsync")).Should().Be(30);
        (await Invoke<Task<int>>(repository, "MinAsync")).Should().Be(10);
    }

    [Fact]
    public async Task DictionariesAndLookupsHonorExplicitDuplicatePolicyAndOrdering()
    {
        var repository = Repository();
        var dictionary = await Invoke<Task<IReadOnlyDictionary<int, string>>>(repository, "DictionaryAsync");
        dictionary[1].Should().Be("apple");
        Func<Task> duplicate = () => Invoke<Task<IReadOnlyDictionary<string, int>>>(repository, "DuplicateDictionaryAsync");
        await duplicate.Should().ThrowAsync<ArgumentException>();
        (await Invoke<Task<IReadOnlyDictionary<string, int>>>(repository, "KeepFirstAsync"))["fruit"].Should().Be(1);
        (await Invoke<Task<IReadOnlyDictionary<string, int>>>(repository, "KeepLastAsync"))["fruit"].Should().Be(3);
        (await Invoke<Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>>(repository, "LookupAsync"))["fruit"].Should().Equal("apple", "pear");
    }

    [Fact]
    public async Task StreamsAreColdSnapshotOncePerEnumerationAndHonorEarlyDisposal()
    {
        var repository = Repository();
        var script = repository.GetType().GetProperty("OnStreamAsync")!.GetValue(repository)!;
        script.GetType().GetProperty("InvocationCount")!.GetValue(script).Should().Be(0L);
        var stream = Invoke<IAsyncEnumerable<PortableView>>(repository, "StreamAsync");
        Store(repository).Seed(new() { Id = 5, Name = "before enumeration", Price = 1 });
        await using (var iterator = stream.GetAsyncEnumerator())
        {
            (await iterator.MoveNextAsync()).Should().BeTrue();
            Store(repository).Clear();
            var ids = new List<int> { iterator.Current.Id };
            while (await iterator.MoveNextAsync()) ids.Add(iterator.Current.Id);
            ids.Should().Equal(1, 2, 3, 5);
        }
        var empty = new List<PortableView>();
        await foreach (var row in stream) empty.Add(row);
        empty.Should().BeEmpty();
        script.GetType().GetProperty("InvocationCount")!.GetValue(script).Should().Be(2L);
    }

    [Fact]
    public async Task ScriptOverridesResetAndCancellationComposeWithGeneratedReads()
    {
        var repository = Repository();
        ScriptedTestDoubleTestHarness.Returns(repository, "OnFindAsync", new PortableView(99, "override", "", 0));
        (await ((IPortableReadPort)repository).FindAsync(1))!.Id.Should().Be(99);
        repository.GetType().GetMethod("ResetTestDouble")!.Invoke(repository, null);
        (await ((IPortableReadPort)repository).FindAsync(1))!.Id.Should().Be(1);
        Store(repository).Count.Should().Be(4);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        Func<Task> call = () => ((IPortableReadPort)repository).FindAsync(1, canceled.Token);
        await call.Should().ThrowAsync<OperationCanceledException>();
    }
}
