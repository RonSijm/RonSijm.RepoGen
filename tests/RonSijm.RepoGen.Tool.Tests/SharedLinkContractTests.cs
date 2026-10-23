using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.SharedLinkTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SharedLinkContractTests
{
    private static readonly int[] OriginalTargetIds = [1, 2, 4];
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSameDesignerMatchesActualGeneratedSqliteForSharedReferencesAndSharedSaving(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new SharedLinkContext(new DbContextOptionsBuilder<SharedLinkContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Owners()); context.AddRange(Targets()); context.AddRange(Links()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.SharedLinkOwnerRepositoryOperations")!, context)!;
        var (fake, data) = Repositories(mapped);
        async Task Equal<T>(string operation, params object?[] inputs)
            => JsonSerializer.Serialize(await Invoke<T>(fake, operation, inputs)).Should().Be(JsonSerializer.Serialize(await Invoke<T>(real, operation, inputs)), operation);
        async Task State()
        {
            var stored = await context.Set<SharedLinkRow>().AsNoTracking().OrderBy(row => row.Scope).ThenBy(row => row.OwnerId).ThenBy(row => row.TargetId)
                .Select(row => new SharedLinkRowData(row.Scope, row.OwnerId, row.TargetId, row.Note, row.Version, row.Hidden)).ToListAsync();
            Rows(data, mapped).Should().Equal(stored);
            await Equal<SharedLinkView?>("FindAsync", new SharedLinkRequest(1, 1, []));
        }
        await Equal<CommandResult<SharedLinkView>>("ReplaceAsync", new SharedLinkRequest(1, 1, [new(1, 1, "changed"), new(1, 3, "new")]));
        await State();
        await Equal<CommandResult<SharedLinkView>>("ReplaceAsync", new SharedLinkRequest(1, 1, [], 1));
        await Equal<CommandResult<SharedLinkView>>("MergeAsync", new SharedLinkRequest(1, 1, [new(1, 1, "stale", 99)], 2));
        await State();
        foreach (var repository in new[] { fake, real })
        {
            Func<Task> reject = async () => await Invoke<CommandResult<SharedLinkView>>(repository, "ReplaceAsync", new SharedLinkRequest(1, 1, [new(1, 1, "valid", 2), new(2, 1, "cross-owner")], 2));
            await reject.Should().ThrowAsync<ArgumentException>().WithMessage("*selected owner*");
        }
        await State();
        await Equal<CommandResult>("StageAsync", new SharedLinkRequest(1, 1, [new(1, 2, "staged")], 2));
        await Equal<CommandResult>("StageNameAsync", 2); await State();
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync()); await State();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal<CommandResult<SharedLinkView>>("ReplaceAsync", new SharedLinkRequest(1, 1, [new(1, 3, "rollback")], 3));
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear(); await State(); context.Set<SharedLinkTarget>().Count().Should().Be(5);
    }

    [Theory]
    [InlineData(true, "owner")]
    [InlineData(false, "owner")]
    [InlineData(true, "mapper")]
    [InlineData(false, "mapper")]
    [InlineData(true, "constraint")]
    [InlineData(false, "constraint")]
    public async Task FailedSharedReferenceCommandsRestoreTrackedRowsAndFixupWhilePreservingEarlierEdits(bool detection, string failure)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new SharedLinkContext(new DbContextOptionsBuilder<SharedLinkContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Owners()); context.AddRange(Targets()); context.AddRange(Links()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var owners = await context.Set<SharedLinkOwner>().Include(row => row.Links).ToListAsync();
        var targets = await context.Set<SharedLinkTarget>().Include(row => row.Links).ToListAsync();
        var selected = owners.Single(row => row.SpaceId == 1); var earlier = owners.Single(row => row.SpaceId == 2);
        earlier.Name = "pending"; context.Entry(earlier).Property(row => row.Name).IsModified = true; context.ChangeTracker.AutoDetectChangesEnabled = detection;
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder().Build());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.SharedLinkOwnerRepositoryOperations")!, context)!;
        Func<Task> execute = failure == "mapper"
            ? async () => await Invoke<CommandResult>(repository, "BadMapAsync", new SharedLinkRequest(1, 1, [new(1, 3, "valid")]))
            : async () => await Invoke<CommandResult<SharedLinkView>>(repository, "ReplaceAsync", new SharedLinkRequest(1, 1,
                [new(1, 1, "changed"), new(failure == "owner" ? 2 : 1, failure == "owner" ? 1 : 3, failure == "constraint" ? "invalid" : "valid")]));
        if (failure == "owner") await execute.Should().ThrowAsync<ArgumentException>().WithMessage("*selected owner*");
        else if (failure == "mapper") await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("*supplied key*");
        else await execute.Should().ThrowAsync<DbUpdateException>();
        context.ChangeTracker.DetectChanges(); selected.Version.Should().Be(1);
        selected.Links.Select(row => row.TargetId).Should().BeEquivalentTo(OriginalTargetIds);
        selected.Links.Single(row => row.TargetId == 1).Note.Should().Be("A");
        targets.Single(row => row.PartitionId == 1 && row.Id == 3).Links.Should().BeEmpty();
        targets.Single(row => row.PartitionId == 2 && row.Id == 1).Links.Should().ContainSingle().Which.Owner.Should().BeSameAs(earlier);
        context.ChangeTracker.Entries<SharedLinkRow>().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        earlier.Name.Should().Be("pending"); context.Entry(earlier).State.Should().Be(EntityState.Modified);
        (await Invoke<CommandResult<SharedLinkView>>(repository, "ReplaceAsync", new SharedLinkRequest(1, 1, [new(1, 1, "valid"), new(1, 3, "valid")]))).Status.Should().Be(CommandStatus.Applied);
        context.ChangeTracker.Clear(); (await context.Set<SharedLinkOwner>().SingleAsync(row => row.SpaceId == 2)).Name.Should().Be("pending");
        context.Set<SharedLinkTarget>().Count().Should().Be(5);
    }
}
