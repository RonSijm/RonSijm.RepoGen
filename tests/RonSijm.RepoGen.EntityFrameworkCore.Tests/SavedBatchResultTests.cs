using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.EntityFrameworkCore.Tests;

public sealed class SavedBatchResultTests
{
    [Fact]
    public async Task CallbackReceivesAllSavedRowsOnceInInputOrder()
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var ids = await store.Context.Customers.OrderByDescending(row => row.Name).Select(row => row.Id).ToArrayAsync();
        var calls = 0;
        var input = TrackedBatchCommandExecutor.PrepareInput(ids, id => id, 5);
        var result = await TrackedBatchCommandExecutor.ExecuteAsync<Customer, Guid, Guid, string>(store.Context, store.Context.Customers, input, row => row.Id,
            (row, _) => { row.Name += " saved"; return [nameof(Customer.Name)]; }, null,
            async (rows, token) =>
            {
                calls++; rows.Select(row => row.Id).Should().Equal(ids);
                rows.Should().OnlyContain(row => store.Context.Entry(row).State == EntityState.Unchanged);
                var query = SavedBatchQuery.Restrict(store.Context.Customers.AsNoTracking(), rows, (entity, saved) => entity.Id == saved.Id);
                var values = await query.Select(row => new { row.Id, row.Name }).ToListAsync(token);
                return SavedBatchQuery.InInputOrder(rows, values, row => [row.Id], value => [value.Id], value => value.Name, token);
            }, false, true, null, []);
        calls.Should().Be(1); result.Status.Should().Be(CommandStatus.Applied); result.Value!.Should().Equal("Ron saved", "Ada saved");
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cardinality")]
    [InlineData("cancel")]
    [InlineData("cancel-returned")]
    public async Task PostSaveFailuresAndCancellationNeverRestoreTheBatch(string mode)
    {
        await using var store = await SqliteTestStore.CreateAsync(); using var cancellation = new CancellationTokenSource();
        var ids = await store.Context.Customers.Select(row => row.Id).ToArrayAsync();
        var input = TrackedBatchCommandExecutor.PrepareInput(ids, id => id, 5);
        Func<Task> call = async () => await TrackedBatchCommandExecutor.ExecuteAsync<Customer, Guid, Guid, string>(store.Context, store.Context.Customers, input, row => row.Id,
            (row, _) => { row.Name = "saved"; return [nameof(Customer.Name)]; }, null,
            (_, token) =>
            {
                if (mode == "cardinality") return Task.FromResult<IReadOnlyList<string>>([]);
                if (mode == "cancel") { cancellation.Cancel(); return Task.FromCanceled<IReadOnlyList<string>>(token); }
                if (mode == "cancel-returned") { cancellation.Cancel(); return Task.FromResult<IReadOnlyList<string>>(["one", "two"]); }
                return Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("failed result"));
            }, false, true, null, [], cancellation.Token);
        await call.Should().ThrowAsync<RepositorySavedResultException>();
        (await store.Context.Customers.AsNoTracking().ToListAsync()).Should().OnlyContain(row => row.Name == "saved");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task StagingAndDeletionRejectSavedQueriesBeforeMutation(bool save, bool delete)
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var ids = await store.Context.Customers.Select(row => row.Id).ToArrayAsync();
        var input = TrackedBatchCommandExecutor.PrepareInput(ids, id => id, 5);
        Func<Task> call = async () => await TrackedBatchCommandExecutor.ExecuteAsync<Customer, Guid, Guid, string>(store.Context, store.Context.Customers, input, row => row.Id,
            (row, _) => { row.Name = "unexpected"; return [nameof(Customer.Name)]; }, null,
            (_, _) => Task.FromResult<IReadOnlyList<string>>([]), delete, save, null, []);
        await call.Should().ThrowAsync<ArgumentException>(); store.Context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public void ResultCorrelationUsesStructuralBinaryKeysNotArrayIdentity()
    {
        var result = SavedBatchQuery.InInputOrder<byte[], (byte[] Key, string Value), string>([[2], [1]], [([1], "one"), ([2], "two")],
            row => [row], row => [row.Key], row => row.Value);
        result.Should().Equal("two", "one");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("extra")]
    public void InvalidResultKeyCoverageFailsInsteadOfReturningAPartialBatch(string mode)
    {
        IReadOnlyList<int> projected = mode switch { "missing" => [1], "duplicate" => [1, 1], _ => [1, 2, 3] };
        Action correlate = () => SavedBatchQuery.InInputOrder<int, int, int>([2, 1], projected, row => [row], row => [row], row => row);
        correlate.Should().Throw<Exception>();
    }
}
