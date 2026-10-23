using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.CompositeCollectionTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheActualGeneratedSqliteAndFixtureOperationsFollowTheSameCompositeMatchingRules(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new CompositeCollectionContext(new DbContextOptionsBuilder<CompositeCollectionContext>().UseSqlite(connection)
            .ReplaceService<IModelCacheKeyFactory, CompositeCollectionModelCacheKeyFactory>().Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Parents()); context.AddRange(Children());
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.CompositeCollectionParentRepositoryOperations")!, context)!;
        var realChild = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.CompositeCollectionChildRepositoryOperations")!, context)!;
        var (fake, fakeChild, data) = Repositories(mapped);
        async Task Equal<TResult>(object first, object second, string method, object input)
        {
            JsonSerializer.Serialize(await Invoke<TResult>(second, method, input)).Should().Be(JsonSerializer.Serialize(await Invoke<TResult>(first, method, input)));
        }
        async Task EqualState()
        {
            foreach (var tenant in new[] { 10, 20 })
            {
                var request = new CompositeCollectionRequest(tenant, 1, []);
                await Equal<IReadOnlyList<CompositeCollectionView>>(realChild, fakeChild, "ListAsync", request);
                await Equal<int?>(real, fake, "VersionAsync", request);
            }
            var actual = await context.Set<CompositeCollectionChild>().AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new CompositeCollectionChildData(row.Id, row.TenantId, row.ParentId, row.Code, row.Language, row.Text, row.Version, row.Hidden)).ToListAsync();
            var expected = mapped ? Store<CompositeCollectionChildData, int>(data).Snapshot().OrderBy(row => row.Id).ToList() :
                Store<CompositeCollectionChild, int>(data).Snapshot().OrderBy(row => row.Id)
                    .Select(row => new CompositeCollectionChildData(row.Id, row.TenantId, row.ParentId, row.Code, row.Language, row.Text, row.Version, row.Hidden)).ToList();
            JsonSerializer.Serialize(expected).Should().Be(JsonSerializer.Serialize(actual));
        }
        await Equal<CommandResult<int>>(real, fake, "ReplaceAsync", new CompositeCollectionRequest(10, 1,
            [new(1, "title", "en", "new English"), new(2, "title", "nl", "new Dutch"), new(6, "title", "fr", "new French")]));
        await EqualState();
        await Equal<CommandResult<int>>(real, fake, "MergeAsync", new CompositeCollectionRequest(10, 1, [new(1, "title", "en", "merged", 2)], 2));
        await EqualState();
        await Equal<CommandResult<int>>(real, fake, "ReplaceAsync", new CompositeCollectionRequest(99, 1, []));
        await Equal<CommandResult<int>>(real, fake, "ReplaceAsync", new CompositeCollectionRequest(10, 1, [], 1));
        await Equal<CommandResult<int>>(real, fake, "ReplaceAsync", new CompositeCollectionRequest(10, 1, [new(2, "title", "nl", "stale", 1)], 3));
        await EqualState();
        await Equal<CommandResult>(real, fake, "StageAsync", new CompositeCollectionRequest(10, 1, [], 3));
        await Equal<CommandResult>(realChild, fakeChild, "StageTextAsync", 5);
        await EqualState();
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync());
        await EqualState();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal<CommandResult<int>>(real, fake, "ReplaceAsync", new CompositeCollectionRequest(10, 1, [new(7, "title", "en", "rollback")], 4));
            await EqualState(); await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear(); await EqualState();
    }
}
