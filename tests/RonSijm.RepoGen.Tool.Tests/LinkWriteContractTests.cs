using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.LinkWriteTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class LinkWriteContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSameDesignerCommandsMatchSqliteForCompositeReferencesPayloadAndSharedSaving(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new LinkWriteContext(new DbContextOptionsBuilder<LinkWriteContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Owners()); context.AddRange(Targets()); context.AddRange(Links());
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder(mapped).Build());
        var realOwner = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.LinkWriteOwnerRepositoryOperations")!, context)!;
        var realTarget = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.LinkWriteTargetRepositoryOperations")!, context)!;
        var (fakeOwner, fakeTarget, data) = Repositories(mapped);
        async Task Equal<T>(object real, object fake, string operation, params object?[] inputs)
            => JsonSerializer.Serialize(await Invoke<T>(fake, operation, inputs)).Should().Be(JsonSerializer.Serialize(await Invoke<T>(real, operation, inputs)), operation);
        async Task State()
        {
            var stored = await context.Set<LinkWriteRow>().AsNoTracking().OrderBy(row => row.OwnerTenantId).ThenBy(row => row.TargetRegion).ThenBy(row => row.TargetCode)
                .Select(row => new LinkWriteRowData(row.OwnerTenantId, row.OwnerId, row.TargetRegion, row.TargetCode, row.Note, row.Version, row.Hidden)).ToListAsync();
            Rows(data, mapped).Should().Equal(stored);
            await Equal<LinkWriteView?>(realOwner, fakeOwner, "FindAsync", new LinkWriteRequest(1, 1, []));
            await Equal<IReadOnlyList<int>>(realTarget, fakeTarget, "UsageAsync");
        }
        await Equal<CommandResult<LinkWriteView>>(realOwner, fakeOwner, "ReplaceAsync", new LinkWriteRequest(1, 1, [new("eu", "a", "changed"), new("us", "a", "new")]));
        await State();
        await Equal<CommandResult<LinkWriteView>>(realOwner, fakeOwner, "ReplaceAsync", new LinkWriteRequest(1, 1, [], 1));
        await Equal<CommandResult<LinkWriteView>>(realOwner, fakeOwner, "MergeAsync", new LinkWriteRequest(1, 1, [new("eu", "a", "stale", 99)], 2));
        await State();
        await Equal<CommandResult>(realOwner, fakeOwner, "StageAsync", new LinkWriteRequest(1, 1, [new("eu", "b", "staged")], 2));
        await Equal<CommandResult>(realOwner, fakeOwner, "StageNameAsync", 2);
        await State();
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync());
        await State();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal<CommandResult<LinkWriteView>>(realOwner, fakeOwner, "ReplaceAsync", new LinkWriteRequest(1, 1, [new("us", "a", "rollback")], 3));
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear(); await State();
        context.Set<LinkWriteTarget>().Count().Should().Be(4); context.Set<LinkWriteOwner>().Count().Should().Be(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedSaveRestoresJoinRowsAndRelationshipFixupWithoutDiscardingEarlierPendingEdits(bool automaticDetection)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new LinkWriteContext(new DbContextOptionsBuilder<LinkWriteContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Owners()); context.AddRange(Targets()); context.AddRange(Links());
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var owners = await context.Set<LinkWriteOwner>().Include(row => row.Links).Include(row => row.Tags).ToListAsync();
        var targets = await context.Set<LinkWriteTarget>().Include(row => row.Links).Include(row => row.Owners).ToListAsync();
        var first = owners.Single(row => row.TenantId == 1); var earlier = owners.Single(row => row.TenantId == 2);
        earlier.Name = "pending"; context.Entry(earlier).Property(row => row.Name).IsModified = true;
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder().Build());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.LinkWriteOwnerRepositoryOperations")!, context)!;
        var request = new LinkWriteRequest(1, 1, [new("eu", "a", "changed"), new("us", "a", "invalid")]);
        Func<Task> save = async () => await Invoke<CommandResult<LinkWriteView>>(repository, "ReplaceAsync", request);
        await save.Should().ThrowAsync<DbUpdateException>();
        context.ChangeTracker.DetectChanges();
        first.Version.Should().Be(1);
        first.Links.Select(row => row.TargetCode).Should().BeEquivalentTo("a", "b", "h");
        first.Tags.Select(row => row.Code).Should().BeEquivalentTo("a", "b", "h");
        targets.Single(row => row.Region == "us").Links.Should().BeEmpty();
        targets.Single(row => row.Region == "us").Owners.Should().BeEmpty();
        targets.Single(row => row.Code == "b").Owners.Should().Contain(first);
        context.ChangeTracker.Entries<LinkWriteRow>().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        earlier.Name.Should().Be("pending"); context.Entry(earlier).State.Should().Be(EntityState.Modified);
        (await Invoke<CommandResult<LinkWriteView>>(repository, "ReplaceAsync", request with { Items = [new("eu", "a", "changed"), new("us", "a", "valid")] })).Status.Should().Be(CommandStatus.Applied);
        context.ChangeTracker.Clear();
        (await context.Set<LinkWriteOwner>().SingleAsync(row => row.TenantId == 2)).Name.Should().Be("pending");
        context.Set<LinkWriteTarget>().Count().Should().Be(4);
    }

    [Fact]
    public async Task DatabaseForeignKeysStayAuthoritativeWithoutCreatingMissingTargets()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new LinkWriteContext(new DbContextOptionsBuilder<LinkWriteContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Owners()); context.AddRange(Targets()); context.AddRange(Links()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder().Build());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.LinkWriteOwnerRepositoryOperations")!, context)!;
        Func<Task> write = async () => await Invoke<CommandResult<LinkWriteView>>(repository, "MergeAsync", new LinkWriteRequest(1, 1, [new("unknown", "id", "missing target")]));
        await write.Should().ThrowAsync<DbUpdateException>();
        context.Set<LinkWriteTarget>().Count().Should().Be(4); context.Set<LinkWriteRow>().Count().Should().Be(4);
        context.ChangeTracker.Entries<LinkWriteRow>().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
    }

    [Theory]
    [InlineData("automaticTransactions")]
    [InlineData("savepoints")]
    public async Task UnsafeSaveBoundariesAreRejectedBeforeAnyTrackedMutation(string disabled)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new LinkWriteContext(new DbContextOptionsBuilder<LinkWriteContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        if (disabled == "automaticTransactions") context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        else context.Database.AutoSavepointsEnabled = false;
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder().Build());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.LinkWriteOwnerRepositoryOperations")!, context)!;
        Func<Task> write = async () => await Invoke<CommandResult<LinkWriteView>>(repository, "ReplaceAsync", new LinkWriteRequest(1, 1, []));
        await write.Should().ThrowAsync<InvalidOperationException>().WithMessage("*saves*require*");
        context.ChangeTracker.Entries().Should().BeEmpty();
        (await Invoke<CommandResult>(repository, "OptionalAsync", new LinkWriteOptionalRequest(1, 1, default))).Status.Should().Be(CommandStatus.NotFound);
    }
}
