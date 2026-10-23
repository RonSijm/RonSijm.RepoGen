using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.OverlappingReferenceTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OverlappingReferenceContractTests
{
    private static readonly int[] OriginalTargets = [1, 2, 4];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActualGeneratedSqliteMatchesFixturesForSharedAdditionalReferencesAndSavedReads(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new OverlappingReferenceContext(new DbContextOptionsBuilder<OverlappingReferenceContext>().UseSqlite(connection).Options);
        await Seed(context);
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.OverlappingReferenceOwnerRepositoryOperations")!, context)!;
        var (fake, data) = Repositories(mapped);
        async Task Equal<T>(string operation, params object?[] inputs)
            => JsonSerializer.Serialize(await Invoke<T>(fake, operation, inputs)).Should().Be(JsonSerializer.Serialize(await Invoke<T>(real, operation, inputs)), operation);
        async Task State()
        {
            var rows = (await context.Set<OverlappingReferenceLink>().AsNoTracking().OrderBy(row => row.Scope).ThenBy(row => row.TargetId).ToListAsync()).Select(Row);
            Rows(data, mapped).Should().Equal(rows);
            await Equal<OverlappingReferenceView?>("FindAsync", new OverlappingReferenceRequest(1, 10, 1, []));
        }
        await Equal<CommandResult<OverlappingReferenceView>>("ReplaceAsync", new OverlappingReferenceRequest(1, 10, 1,
            [new(1, 1, 999, "ignored", "ignored", 999, "changed"), new(1, 3, 10, "b", "a", 2, "new")]));
        await State();
        foreach (var repository in new[] { fake, real })
        {
            Func<Task> wrongOwner = async () => await Invoke<CommandResult>(repository, "StageAsync", new OverlappingReferenceRequest(1, 10, 1,
                [new(1, 1, 999, "ignored", "ignored", 999, "stale", 99), new(1, 2, 20, "a", "b", 1, "wrong zone")], 2));
            await wrongOwner.Should().ThrowAsync<ArgumentException>().WithMessage("*selected owner*before ownership*");
        }
        await State();
        await Equal<CommandResult>("StageAsync", new OverlappingReferenceRequest(1, 10, 1, [new(1, 2, 10, "a", "b", 1, "staged")], 2));
        await Equal<CommandResult>("StageNameAsync", 2); await State();
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync()); await State();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal<CommandResult<OverlappingReferenceView>>("ReplaceAsync", new OverlappingReferenceRequest(1, 10, 1, [new(1, 3, 10, "a", "b", 1, "rollback")], 3));
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear(); await State();
        context.Set<OverlappingReferenceActor>().Count().Should().Be(3); context.Set<OverlappingReferenceSource>().Count().Should().Be(3);
    }

    [Theory]
    [InlineData(true, "owner")]
    [InlineData(false, "owner")]
    [InlineData(true, "mapper")]
    [InlineData(false, "mapper")]
    [InlineData(true, "constraint")]
    [InlineData(false, "constraint")]
    [InlineData(true, "reference")]
    [InlineData(false, "reference")]
    public async Task FailedCommandsRestoreTrackedSharedKeysFixupAndEarlierUnrelatedWork(bool detection, string failure)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new OverlappingReferenceContext(new DbContextOptionsBuilder<OverlappingReferenceContext>().UseSqlite(connection).Options);
        await Seed(context);
        var owners = await context.Set<OverlappingReferenceOwner>().Include(row => row.Links).ToListAsync();
        await context.Set<OverlappingReferenceActor>().LoadAsync(); await context.Set<OverlappingReferenceSource>().LoadAsync();
        var selected = owners.Single(row => row.PartitionId == 1); var earlier = owners.Single(row => row.PartitionId == 2);
        earlier.Name = "pending"; context.Entry(earlier).Property(row => row.Name).IsModified = true; context.ChangeTracker.AutoDetectChangesEnabled = detection;
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder().Build());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.OverlappingReferenceOwnerRepositoryOperations")!, context)!;
        Func<Task> fail = failure == "mapper"
            ? async () => await Invoke<CommandResult>(repository, "BadMapAsync", new OverlappingReferenceRequest(1, 10, 1, [new(1, 3, 10, "a", "b", 1, "new")]))
            : async () => await Invoke<CommandResult<OverlappingReferenceView>>(repository, "ReplaceAsync", new OverlappingReferenceRequest(1, 10, 1,
                [new(1, 1, 999, "ignored", "ignored", 999, "changed"), new(1, 3, failure == "owner" ? 20 : 10, failure == "reference" ? "missing" : "a", "b", 1, failure == "constraint" ? "invalid" : "new")]));
        if (failure is "owner" or "mapper") await fail.Should().ThrowAsync<ArgumentException>().WithMessage("*selected owner*before ownership*");
        else await fail.Should().ThrowAsync<DbUpdateException>();
        context.ChangeTracker.DetectChanges(); selected.Version.Should().Be(1);
        selected.Links.Select(row => row.TargetId).Should().BeEquivalentTo(OriginalTargets);
        selected.Links.Single(row => row.TargetId == 1).Note.Should().Be("A");
        selected.Links.Should().OnlyContain(row => row.Scope == 1 && row.Zone == 10);
        context.ChangeTracker.Entries<OverlappingReferenceLink>().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        earlier.Name.Should().Be("pending"); context.Entry(earlier).State.Should().Be(EntityState.Modified);
        (await Invoke<CommandResult<OverlappingReferenceView>>(repository, "ReplaceAsync", new OverlappingReferenceRequest(1, 10, 1,
            [new(1, 1, 10, "a", "b", 1, "valid"), new(1, 3, 10, "b", "a", 2, "valid")]))).Status.Should().Be(CommandStatus.Applied);
        context.ChangeTracker.Clear(); (await context.Set<OverlappingReferenceOwner>().SingleAsync(row => row.PartitionId == 2)).Name.Should().Be("pending");
    }

    private static async Task Seed(OverlappingReferenceContext context)
    {
        await context.Database.EnsureCreatedAsync(); context.AddRange(Owners()); context.AddRange(Targets()); context.AddRange(Actors()); context.AddRange(Sources()); context.AddRange(Links());
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
    }
}
