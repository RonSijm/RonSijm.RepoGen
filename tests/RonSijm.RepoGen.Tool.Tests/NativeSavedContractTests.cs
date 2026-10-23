using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NativeSavedTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NativeSavedContractTests
{
    [Theory]
    [InlineData("Rewrite", 1, 1, false)]
    [InlineData("Rewrite", 2, 1, false)]
    [InlineData("Rewrite", 1, 99, false)]
    [InlineData("Rewrite", 99, 1, false)]
    [InlineData("BypassHide", 1, 1, true)]
    public async Task ActualGeneratedSqliteAndFixtureReturnTheSameFreshGraphs(string operation, int parent, int version, bool hidden)
    {
        await using var context = MemoryCollectionTestHarness.Context();
        await SeedAsync(context);
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MemoryCollectionChildRepositoryOperations")!, context, new SqliteReturningWriter())!;
        var (memory, _, _) = Fixture();
        var request = new NativeSavedRequest(parent, ExpectedVersion: version, Hidden: hidden);
        var actual = await Call<MutationResult<NativeSavedView>>(real, operation, request);
        var expected = await Call<MutationResult<NativeSavedView>>(memory, operation, request);
        actual.AffectedRows.Should().Be(expected.AffectedRows);
        actual.Items.Should().BeEquivalentTo(expected.Items);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData("TightBound", false, false)]
    [InlineData("TightBound", false, true)]
    [InlineData("Fail", false, false)]
    [InlineData("Fail", false, true)]
    [InlineData("Hide", true, false)]
    [InlineData("Hide", true, true)]
    public async Task ResultFailuresRollBackOwnedTransactionsOrOnlyTheBorrowedOperationSavepoint(string operation, bool hidden, bool borrowed)
    {
        await using var context = MemoryCollectionTestHarness.Context();
        await SeedAsync(context);
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MemoryCollectionChildRepositoryOperations")!, context, new SqliteReturningWriter())!;
        await using var transaction = borrowed ? await context.Database.BeginTransactionAsync() : null;
        if (borrowed)
        {
            await context.Set<MemoryCollectionChild>().Where(row => row.Id == 4).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Text, "earlier"));
        }
        var pending = await context.Set<MemoryCollectionParent>().SingleAsync(row => row.Id == 1);
        pending.Name = "pending parent";
        var request = new NativeSavedRequest(1, Hidden: hidden);
        Func<Task> fail = () => operation == "Fail"
            ? Call<MutationResult<SavedResultRejectedView>>(repository, operation, request)
            : Call<MutationResult<NativeSavedView>>(repository, operation, request);
        await fail.Should().ThrowAsync<InvalidOperationException>();
        (await context.Set<MemoryCollectionChild>().AsNoTracking().Where(row => row.Id == 1).Select(row => row.Text).SingleAsync()).Should().Be("A");
        (await context.Set<MemoryCollectionChild>().AsNoTracking().Where(row => row.Id == 4).Select(row => row.Text).SingleAsync()).Should().Be(borrowed ? "earlier" : "C");
        context.ChangeTracker.Entries().Should().ContainSingle().Which.State.Should().Be(EntityState.Modified);
        if (borrowed)
        {
            context.Database.CurrentTransaction.Should().BeSameAs(transaction);
            await transaction!.RollbackAsync();
        }
        else
        {
            context.Database.CurrentTransaction.Should().BeNull();
        }
    }

    [Fact]
    public async Task FreshDatabaseResultsIgnorePendingRootAndRelatedTrackedState()
    {
        await using var context = MemoryCollectionTestHarness.Context();
        await SeedAsync(context);
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MemoryCollectionChildRepositoryOperations")!, context, new SqliteReturningWriter())!;
        var parent = await context.Set<MemoryCollectionParent>().SingleAsync(row => row.Id == 1);
        var child = await context.Set<MemoryCollectionChild>().SingleAsync(row => row.Id == 4);
        parent.Name = "pending parent";
        child.Text = "pending child";
        var result = await Call<MutationResult<NativeSavedView>>(repository, "Rewrite", new NativeSavedRequest(1));
        result.Items.Should().OnlyContain(item => item.Parent == "saved parent");
        context.ChangeTracker.Entries().Should().HaveCount(2).And.OnlyContain(entry => entry.State == EntityState.Modified);
        await context.SaveChangesAsync();
        (await context.Set<MemoryCollectionChild>().AsNoTracking().SingleAsync(row => row.Id == 4)).Text.Should().Be("pending child");
    }
}
