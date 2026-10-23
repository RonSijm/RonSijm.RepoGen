using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.OptionalReferenceTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalReferenceContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSameDesignerMatchesActualSqliteForNullableReferencesAndSavedResults(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new OptionalReferenceContext(new DbContextOptionsBuilder<OptionalReferenceContext>().UseSqlite(connection).Options);
        await Seed(context);
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.OptionalReferenceOwnerRepositoryOperations")!, context)!;
        var (fake, data) = Repositories(mapped);
        async Task Equal<T>(string operation, params object?[] inputs)
            => JsonSerializer.Serialize(await Invoke<T>(fake, operation, inputs)).Should().Be(JsonSerializer.Serialize(await Invoke<T>(real, operation, inputs)));
        async Task State()
        {
            var rows = (await context.Set<OptionalReferenceRow>().AsNoTracking().OrderBy(row => row.OwnerId).ThenBy(row => row.TargetId).ToListAsync()).Select(DataOf);
            Rows(data, mapped).Should().Equal(rows); await Equal<OptionalReferenceView?>("FindAsync", 1);
        }
        await Equal<CommandResult<OptionalReferenceView>>("ReplaceAsync", new OptionalReferenceRequest(1,
            [new(1, null, "ignored partial Add-only", null, "changed present"), new(2, 2, "b", 2, "changed absent"), new(3, null, null, null, "new absent"), new(4, 2, "b", 2, "new present")]));
        await State();
        await Equal<CommandResult<OptionalReferenceView>>("MergeAsync", new OptionalReferenceRequest(1, [new(1, null, null, null, "stale", 99)], 2)); await State();
        await Equal<CommandResult>("StageAsync", new OptionalReferenceRequest(1, [new(5, null, null, 2, "staged")], 2));
        await Equal<CommandResult>("StageNameAsync", 2); await State();
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync()); await State();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal<CommandResult<OptionalReferenceView>>("ReplaceAsync", new OptionalReferenceRequest(1, [new(4, 2, "b", null, "rollback")], 3));
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear(); await State();
        context.Set<OptionalReferenceReviewer>().Count().Should().Be(2); context.Set<OptionalReferenceSource>().Count().Should().Be(2);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task NewPartialCompositeReferencesDoNotApplyParentOrMembershipChanges(bool detection, bool firstNull)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new OptionalReferenceContext(new DbContextOptionsBuilder<OptionalReferenceContext>().UseSqlite(connection).Options);
        await Seed(context); var owners = await context.Set<OptionalReferenceOwner>().Include(row => row.Links).ToListAsync();
        var selected = owners.Single(row => row.Id == 1); var earlier = owners.Single(row => row.Id == 2);
        earlier.Name = "pending"; context.Entry(earlier).Property(row => row.Name).IsModified = true; context.ChangeTracker.AutoDetectChangesEnabled = detection;
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder().Build());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.OptionalReferenceOwnerRepositoryOperations")!, context)!;
        Func<Task> fail = async () => await Invoke<CommandResult<OptionalReferenceView>>(repository, "ReplaceAsync", new OptionalReferenceRequest(1,
            [new(1, null, null, null, "must not persist"), new(3, null, null, null, "earlier valid addition"), new(4, firstNull ? null : 2, firstNull ? "b" : null, null, "partial")]));
        await fail.Should().ThrowAsync<ArgumentException>().WithMessage("*Reviewer*entirely null or entirely present*");
        context.ChangeTracker.DetectChanges(); selected.Version.Should().Be(1); selected.Links.Select(row => row.TargetId).Should().BeEquivalentTo([1, 2, 6]);
        selected.Links.Single(row => row.TargetId == 1).Note.Should().Be("present");
        context.ChangeTracker.Entries<OptionalReferenceRow>().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        context.Entry(earlier).State.Should().Be(EntityState.Modified); (await context.SaveChangesAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(true, "reviewer")]
    [InlineData(false, "reviewer")]
    [InlineData(true, "source")]
    [InlineData(false, "source")]
    [InlineData(true, "constraint")]
    [InlineData(false, "constraint")]
    public async Task PresentButInvalidForeignKeysAndSaveFailuresRestoreOptionalFixup(bool detection, string failure)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new OptionalReferenceContext(new DbContextOptionsBuilder<OptionalReferenceContext>().UseSqlite(connection).Options);
        await Seed(context); var owners = await context.Set<OptionalReferenceOwner>().Include(row => row.Links).ToListAsync();
        var reviewers = await context.Set<OptionalReferenceReviewer>().Include(row => row.Links).ToListAsync();
        var sources = await context.Set<OptionalReferenceSource>().Include(row => row.Links).ToListAsync();
        var selected = owners.Single(row => row.Id == 1); var earlier = owners.Single(row => row.Id == 2);
        earlier.Name = "pending"; context.Entry(earlier).Property(row => row.Name).IsModified = true; context.ChangeTracker.AutoDetectChangesEnabled = detection;
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder().Build());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.OptionalReferenceOwnerRepositoryOperations")!, context)!;
        Func<Task> fail = async () => await Invoke<CommandResult<OptionalReferenceView>>(repository, "ReplaceAsync", new OptionalReferenceRequest(1,
            [new(1, null, null, null, "changed"), new(3, failure == "reviewer" ? 99 : 2, "b", failure == "source" ? 99 : 2, failure == "constraint" ? "invalid" : "new")]));
        await fail.Should().ThrowAsync<DbUpdateException>(); context.ChangeTracker.DetectChanges();
        selected.Version.Should().Be(1); selected.Links.Select(row => row.TargetId).Should().BeEquivalentTo([1, 2, 6]);
        selected.Links.Single(row => row.TargetId == 1).ReviewerRegion.Should().Be(1); selected.Links.Single(row => row.TargetId == 1).Note.Should().Be("present");
        selected.Links.Single(row => row.TargetId == 2).Reviewer.Should().BeNull(); selected.Links.Single(row => row.TargetId == 2).Source.Should().BeNull();
        reviewers.SelectMany(row => row.Links).Should().NotContain(row => row.TargetId == 3); sources.SelectMany(row => row.Links).Should().NotContain(row => row.TargetId == 3);
        context.ChangeTracker.Entries<OptionalReferenceRow>().Should().OnlyContain(entry => entry.State == EntityState.Unchanged); context.Entry(earlier).State.Should().Be(EntityState.Modified);
        (await Invoke<CommandResult<OptionalReferenceView>>(repository, "ReplaceAsync", new OptionalReferenceRequest(1, [new(1, null, null, null, "valid"), new(3, null, null, null, "valid")]))).Status.Should().Be(CommandStatus.Applied);
        context.ChangeTracker.Clear(); (await context.Set<OptionalReferenceOwner>().SingleAsync(row => row.Id == 2)).Name.Should().Be("pending");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task APartialNewReferenceHasTheSameFailurePrecedenceAsSqliteWhenAnotherItemHasAStaleVersion(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new OptionalReferenceContext(new DbContextOptionsBuilder<OptionalReferenceContext>().UseSqlite(connection).Options);
        await Seed(context);
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.OptionalReferenceOwnerRepositoryOperations")!, context)!;
        var (fake, data) = Repositories(mapped); var before = Rows(data, mapped);
        var request = new OptionalReferenceRequest(1, [new(1, null, null, null, "stale", 99), new(3, 2, null, null, "partial")]);
        Func<Task> fakeFailure = async () => await Invoke<CommandResult<OptionalReferenceView>>(fake, "ReplaceAsync", request);
        Func<Task> realFailure = async () => await Invoke<CommandResult<OptionalReferenceView>>(real, "ReplaceAsync", request);
        await fakeFailure.Should().ThrowAsync<ArgumentException>().WithMessage("*Reviewer*entirely null or entirely present*");
        await realFailure.Should().ThrowAsync<ArgumentException>().WithMessage("*Reviewer*entirely null or entirely present*");
        Rows(data, mapped).Should().Equal(before); data.PendingCount.Should().Be(0);
        (await context.Set<OptionalReferenceOwner>().AsNoTracking().SingleAsync(row => row.Id == 1)).Version.Should().Be(1);
        (await context.Set<OptionalReferenceRow>().AsNoTracking().SingleAsync(row => row.OwnerId == 1 && row.TargetId == 1)).Note.Should().Be("present");
    }

    private static async Task Seed(OptionalReferenceContext context)
    {
        await context.Database.EnsureCreatedAsync(); context.AddRange(Owners()); context.AddRange(Targets()); context.AddRange(Reviewers()); context.AddRange(Sources()); context.AddRange(Links());
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
    }
}
