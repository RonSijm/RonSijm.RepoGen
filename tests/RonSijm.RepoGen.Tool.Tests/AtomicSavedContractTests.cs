using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AtomicSavedTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicSavedContractTests
{
    [Theory]
    [InlineData("Ensure", "c", 1, false)]
    [InlineData("Put", "a", 1, false)]
    [InlineData("Put", "a", 99, false)]
    [InlineData("Ensure", "a", 1, false)]
    [InlineData("Put", "hidden", 1, false)]
    [InlineData("BypassHide", "a", 1, true)]
    public async Task GeneratedSqliteAndFixtureAgreeOnSavedGraphs(string operation, string key, int version, bool hidden)
    {
        await using var context = MemoryCollectionTestHarness.Context();
        await SeedAsync(context);
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MemoryCollectionChildRepositoryOperations")!, context, new SqliteAtomicWriter())!;
        var (memory, _, _) = Fixture();
        var request = new AtomicSavedRequest(1, key, "changed", version, hidden);
        var actual = await Call<CommandResult<AtomicSavedView>>(real, operation, request);
        var expected = await Call<CommandResult<AtomicSavedView>>(memory, operation, request);
        JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected));
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData("FailEnsure", "new")]
    [InlineData("FailPut", "a")]
    [InlineData("Ensure", "new", true)]
    [InlineData("Hide", "a", true)]
    public async Task ResultFailureRollsBackOwnedDatabaseTransaction(string operation, string key, bool hidden = false)
    {
        await using var context = MemoryCollectionTestHarness.Context();
        await SeedAsync(context);
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MemoryCollectionChildRepositoryOperations")!, context, new SqliteAtomicWriter())!;
        var pending = Parents()[0];
        pending.Id = 50;
        context.Add(pending);
        var before = await context.Set<MemoryCollectionChild>().AsNoTracking().OrderBy(row => row.Id).Select(row => row.Text).ToListAsync();
        Func<Task> failure = () => operation.StartsWith("Fail", StringComparison.Ordinal)
            ? Call<CommandResult<SavedResultRejectedView>>(repository, operation, new AtomicSavedRequest(1, key, "fail", Hidden: hidden))
            : Call<CommandResult<AtomicSavedView>>(repository, operation, new AtomicSavedRequest(1, key, "fail", Hidden: hidden));
        await failure.Should().ThrowAsync<InvalidOperationException>();
        (await context.Set<MemoryCollectionChild>().AsNoTracking().OrderBy(row => row.Id).Select(row => row.Text).ToListAsync()).Should().Equal(before);
        context.Database.CurrentTransaction.Should().BeNull();
        context.ChangeTracker.Entries().Should().ContainSingle().Which.State.Should().Be(EntityState.Added);
        (await context.Set<MemoryCollectionParent>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task FreshDatabaseReadIgnoresTrackedIdentityAndPendingRelatedChanges()
    {
        await using var context = MemoryCollectionTestHarness.Context();
        await SeedAsync(context);
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MemoryCollectionChildRepositoryOperations")!, context, new SqliteAtomicWriter())!;
        var parent = await context.Set<MemoryCollectionParent>().SingleAsync();
        var child = await context.Set<MemoryCollectionChild>().SingleAsync(row => row.Id == 2);
        parent.Name = "pending parent";
        child.Text = "pending child";
        var result = await Call<CommandResult<AtomicSavedView>>(repository, "Put", new AtomicSavedRequest(1, "a", "changed"));
        result.Value!.ParentName.Should().Be("saved parent");
        result.Value.Siblings.Should().Equal("changed", "B");
        context.ChangeTracker.Entries().Should().HaveCount(2).And.OnlyContain(entry => entry.State == EntityState.Modified);
        await context.SaveChangesAsync();
        (await context.Set<MemoryCollectionParent>().AsNoTracking().SingleAsync()).Name.Should().Be("pending parent");
    }

    [Fact]
    public async Task CallerOwnedTransactionRetainsExplicitRollbackResponsibility()
    {
        await using var context = MemoryCollectionTestHarness.Context();
        await SeedAsync(context);
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MemoryCollectionChildRepositoryOperations")!, context, new SqliteAtomicWriter())!;
        await using var transaction = await context.Database.BeginTransactionAsync();
        Func<Task> failure = () => Call<CommandResult<SavedResultRejectedView>>(repository, "FailPut", new AtomicSavedRequest(1, "a", "changed"));
        await failure.Should().ThrowAsync<InvalidOperationException>();
        (await context.Set<MemoryCollectionChild>().AsNoTracking().SingleAsync(row => row.Id == 1)).Text.Should().Be("changed");
        context.Database.CurrentTransaction.Should().BeSameAs(transaction);
        await transaction.RollbackAsync();
        (await context.Set<MemoryCollectionChild>().AsNoTracking().SingleAsync(row => row.Id == 1)).Text.Should().Be("A");
    }
}
