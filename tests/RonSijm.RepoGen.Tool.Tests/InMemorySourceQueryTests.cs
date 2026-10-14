using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemorySourceQueryTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemorySourceQueryTests
{
    [Theory]
    [InlineData("Inner", new[] { 1, 2, 5, 6 })]
    [InlineData("Composite", new[] { 1, 5, 6 })]
    [InlineData("BypassCustomers", new[] { 1, 2, 3, 5, 6 })]
    public async Task JoinInputsApplyEachVisibilityPolicyBeforeComposition(string operation, int[] ids)
    {
        var repository = Repository();
        var rows = await Invoke<Task<IReadOnlyList<PortableSourceView>>>(repository, operation + "Async");
        rows.Select(row => row.OrderId).Should().Equal(ids);
        rows.Should().NotContain(row => row.OrderId == 7);
    }

    [Fact]
    public async Task LeftJoinsRetainUnmatchedAndFilteredRightRowsAsNulls()
    {
        var rows = await Invoke<Task<IReadOnlyList<PortableSourceView>>>(Repository(), "LeftAsync");
        rows.Select(row => row.OrderId).Should().Equal(1, 2, 3, 4, 5, 6);
        rows.Select(row => row.Customer).Should().Equal("North", null, null, null, "South", "North");
    }

    [Theory]
    [InlineData("Matched", new[] { 1, 5, 6 })]
    [InlineData("Unmatched", new[] { 2, 3, 4 })]
    [InlineData("Self", new[] { 1, 2, 3, 4, 5, 6 })]
    [InlineData("Union", new[] { 1, 2, 3, 4, 5, 6 })]
    [InlineData("Concat", new[] { 1, 1, 2, 2, 3, 4, 5, 6 })]
    [InlineData("Except", new[] { 3, 4, 5, 6 })]
    [InlineData("Intersect", new[] { 1, 2 })]
    public async Task CorrelationSelfJoinsAndSetOperationsUseTheDeclaredInputs(string operation, int[] ids)
        => (await Invoke<Task<IReadOnlyList<int>>>(Repository(), operation + "Async")).Should().Equal(ids);

    [Fact]
    public async Task GroupsRequestsGuardsAndDictionaryTerminalsExecuteOverJoinedResults()
    {
        var repository = Repository();
        var page = await Invoke<Task<PagedResult<PortableSourceTotal>>>(repository, "TotalsAsync", new PortableSourceRequest(MinimumCount: 2), 1, 10);
        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Should().Be(new PortableSourceTotal("North", 3, 90));
        var minimum = await Invoke<Task<PagedResult<PortableSourceTotal>>>(repository, "TotalsAsync", new PortableSourceRequest(MinimumAmount: 55), 1, 10);
        minimum.Items.Should().ContainSingle().Which.Total.Should().Be(60);
        Func<Task> invalid = () => Invoke<Task<PagedResult<PortableSourceTotal>>>(repository, "TotalsAsync", new PortableSourceRequest(MinimumAmount: -1), 1, 10);
        await invalid.Should().ThrowAsync<ArgumentException>();
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(repository, "NamesAsync"))
            .Should().BeEquivalentTo(new Dictionary<int, string> { [1] = "North", [2] = "North", [5] = "South", [6] = "North" });
        (await Invoke<Task<IReadOnlyList<PortableSourceTotal>>>(repository, "SourceGroupsAsync"))
            .Should().ContainSingle().Which.Should().Be(new PortableSourceTotal("group", 3, 90));
        (await Invoke<Task<IReadOnlyList<string>>>(repository, "PairsAsync")).Should().Equal("North", "South");
    }

    [Fact]
    public async Task ColdStreamsKeepBothSourcesDetachedForTheWholeEnumerationAndReexecuteOnNextEnumeration()
    {
        var repository = Repository();
        var data = Data(repository);
        var customers = data.GetType().GetProperty("PortableSourceCustomerStore")!.GetValue(data).AsStore<PortableSourceCustomer>();
        var stream = Invoke<IAsyncEnumerable<PortableSourceView>>(repository, "StreamAsync");
        customers.Seed(new() { Id = 1, Name = "before first move", Zone = "N" }, Testing.InMemoryDuplicateKeyBehavior.Replace);
        var first = new List<PortableSourceView>();
        await using (var iterator = stream.GetAsyncEnumerator())
        {
            (await iterator.MoveNextAsync()).Should().BeTrue();
            first.Add(iterator.Current);
            customers.Clear();
            while (await iterator.MoveNextAsync()) first.Add(iterator.Current);
        }
        first.Select(row => row.OrderId).Should().Equal(1, 2, 5, 6);
        first[0].Customer.Should().Be("before first move");
        var second = new List<PortableSourceView>();
        await foreach (var row in stream) second.Add(row);
        second.Should().BeEmpty();
    }

    [Fact]
    public async Task ScriptsAndResetContinueToOverrideAndRestoreTheGeneratedBackend()
    {
        var repository = Repository();
        ScriptedTestDoubleTestHarness.Returns(repository, "OnInnerAsync", new List<PortableSourceView> { new(99, "arranged", 0) });
        (await Invoke<Task<IReadOnlyList<PortableSourceView>>>(repository, "InnerAsync")).Should().ContainSingle().Which.OrderId.Should().Be(99);
        repository.GetType().GetMethod("ResetTestDouble")!.Invoke(repository, null);
        (await Invoke<Task<IReadOnlyList<PortableSourceView>>>(repository, "InnerAsync")).Should().HaveCount(4);
    }

    [Fact]
    public async Task ConditionalRootsNullableScalarsSlicesAndResultCursorsKeepTheirContractShapes()
    {
        var repository = Repository();
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "ConditionalAsync", new PortableSourceRequest())).Should().Equal(1, 2, 3, 4, 5, 6);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "ConditionalAsync", new PortableSourceRequest(MinimumAmount: 1))).Should().Equal(1, 2);
        (await Invoke<Task<int>>(repository, "CountAsync")).Should().Be(4);
        (await Invoke<Task<bool>>(repository, "ExistsAsync")).Should().BeTrue();
        (await Invoke<Task<int?>>(repository, "FirstValueAsync")).Should().BeNull();
        (await Invoke<Task<int>>(repository, "MaximumAsync")).Should().Be(60);
        var slice = await Invoke<Task<SliceResult<PortableSourceView>>>(repository, "SliceAsync", 1, 2);
        slice.Items.Select(row => row.OrderId).Should().Equal(1, 2);
        slice.HasMore.Should().BeTrue();
        var cursor = await Invoke<Task<CursorPagedResult<PortableSourceView, int>>>(repository, "CursorAsync", new CursorPageRequest<int>(2, 2));
        cursor.Items.Select(row => row.OrderId).Should().Equal(5, 6);
        cursor.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task CanceledMultiSourceCallsAndStreamsNeverBecomeEmptySuccesses()
    {
        var repository = Repository();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Func<Task> call = () => (Task<IReadOnlyList<PortableSourceView>>)repository.GetType().GetMethod("InnerAsync")!.Invoke(repository, [cancellation.Token])!;
        await call.Should().ThrowAsync<OperationCanceledException>();
        var stream = Invoke<IAsyncEnumerable<PortableSourceView>>(repository, "StreamAsync");
        await using var iterator = stream.GetAsyncEnumerator(cancellation.Token);
        Func<Task> move = async () => await iterator.MoveNextAsync();
        await move.Should().ThrowAsync<OperationCanceledException>();
        (await Invoke<Task<IReadOnlyList<PortableSourceView>>>(repository, "InnerAsync")).Should().HaveCount(4);
    }

    [Fact]
    public async Task SharedMultiSourceCasesAgreeWithActualGeneratedSqliteOperations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new PortableSourceContext(new DbContextOptionsBuilder<PortableSourceContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(Orders());
        context.AddRange(Customers());
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var model = RepositoryModelFactory.Create(context, typeof(PortableSourceOrder).Assembly, design: new PortableSourceRepositoryDesigner().CreateModel());
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var production = Activator.CreateInstance(assembly.GetType("Generated.Sources.Persistence.PortableSourceOrderRepositoryOperations")!, context)!;
        var memory = Repository();
        async Task Equal<TResult>(string name, params object?[] inputs)
        {
            var actual = await Invoke<Task<TResult>>(memory, name, inputs);
            var expected = await Invoke<Task<TResult>>(production, name, inputs);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected), "the same designed contract is exercised on both backends: " + name);
        }
        foreach (var name in new[] { "Inner", "Composite", "Left", "BypassCustomers" }) await Equal<IReadOnlyList<PortableSourceView>>(name + "Async");
        foreach (var name in new[] { "Matched", "Unmatched", "Self", "Union", "Concat", "Except", "Intersect" }) await Equal<IReadOnlyList<int>>(name + "Async");
        await Equal<IReadOnlyList<string>>("PairsAsync");
        await Equal<IReadOnlyList<string>>("CustomersOnlyAsync");
        await Equal<PagedResult<PortableSourceTotal>>("TotalsAsync", new PortableSourceRequest(MinimumCount: 2), 1, 10);
        await Equal<IReadOnlyDictionary<int, string>>("NamesAsync");
        await Equal<IReadOnlyList<PortableSourceTotal>>("SourceGroupsAsync");
        await Equal<IReadOnlyList<int>>("ConditionalAsync", new PortableSourceRequest());
        await Equal<IReadOnlyList<int>>("ConditionalAsync", new PortableSourceRequest(MinimumAmount: 1));
        await Equal<int>("CountAsync");
        await Equal<bool>("ExistsAsync");
        await Equal<int?>("FirstValueAsync");
        await Equal<int>("MaximumAsync");
        await Equal<SliceResult<PortableSourceView>>("SliceAsync", 1, 2);
        await Equal<CursorPagedResult<PortableSourceView, int>>("CursorAsync", new CursorPageRequest<int>(2, 2));
        context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
