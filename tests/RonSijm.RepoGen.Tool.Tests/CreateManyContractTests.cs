using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CreateManyContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualGeneratedSqliteAndFixtureShareFreshOrderedResultsAndSharedSaves(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var capture = new SavedBatchCommandCapture();
        await using var context = new CreateManyContext(new DbContextOptionsBuilder<CreateManyContext>().UseSqlite(connection).AddInterceptors(capture).Options);
        await context.Database.EnsureCreatedAsync();
        context.Add(new CreateManyParent { Id = 7, Name = "old" }); context.Add(new CreateManyRow { Id = 7, ParentId = 7, Name = "old" });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, new CreateManyRepositoryDesigner(mapped).CreateModel());
        var real = Activator.CreateInstance(assembly.GetType("Generated.CreateMany.Persistence.CreateManyRowRepositoryOperations")!, context)!;
        var realParent = Activator.CreateInstance(assembly.GetType("Generated.CreateMany.Persistence.CreateManyParentRepositoryOperations")!, context)!;
        var (fake, fakeParent, _) = CreateManyTestHarness.Fixture(mapped);
        await Invoke<CommandResult>(realParent, "StageNameAsync", 7); await Invoke<CommandResult>(fakeParent, "StageNameAsync", 7); capture.Queries.Clear();
        var request = new CreateManyRequest([new(7, " b "), new(7, " a ")], "label");
        var providerResult = await Invoke<CommandResult<IReadOnlyList<CreateManyView>>>(real, "FreshAsync", request);
        var fixtureResult = await Invoke<CommandResult<IReadOnlyList<CreateManyView>>>(fake, "FreshAsync", request);
        JsonSerializer.Serialize(fixtureResult).Should().Be(JsonSerializer.Serialize(providerResult));
        providerResult.AffectedRows.Should().Be(3); providerResult.Value!.Select(row => row.Name).Should().Equal("b", "a");
        capture.Queries.Count(sql => sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)).Should().Be(1, "fresh results use one batch query, not one read per row");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseConstraintFailureRollsBackTheWholeSaveAndDetachesOnlyNewBatchRows(bool automaticDetection)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new CreateManyContext(new DbContextOptionsBuilder<CreateManyContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.Add(new CreateManyParent { Id = 7, Name = "old" });
        await context.SaveChangesAsync();
        var parent = context.Set<CreateManyParent>().Local.Single(); parent.Name = "pending";
        context.Entry(parent).Property(row => row.Name).IsModified = true;
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var assembly = SavedResultTestHarness.CompileProduction(context, new CreateManyRepositoryDesigner().CreateModel());
        var repository = Activator.CreateInstance(assembly.GetType("Generated.CreateMany.Persistence.CreateManyRowRepositoryOperations")!, context)!;
        Func<Task> call = async () => await Invoke<CommandResult<IReadOnlyList<int>>>(repository, "CreateAsync", new CreateManyRequest([new(7, "duplicate"), new(7, "duplicate")]));
        await call.Should().ThrowAsync<DbUpdateException>();
        context.ChangeTracker.Entries<CreateManyRow>().Should().BeEmpty(); context.Entry(parent).State.Should().Be(EntityState.Modified);
        parent.Rows.Should().BeEmpty("failed insertions cannot remain reachable through a tracked principal");
        (await context.Set<CreateManyRow>().FindAsync(1)).Should().BeNull("failed store-generated keys cannot remain in EF's identity map");
        (await context.Set<CreateManyParent>().AsNoTracking().SingleAsync()).Name.Should().Be("old");
        (await context.Set<CreateManyRow>().CountAsync()).Should().Be(0);
        (await Invoke<CommandResult<IReadOnlyList<int>>>(repository, "CreateAsync", new CreateManyRequest([new(7, "retry")]))).Value.Should().Equal(1);
        context.ChangeTracker.AutoDetectChangesEnabled.Should().Be(automaticDetection);
    }
}
