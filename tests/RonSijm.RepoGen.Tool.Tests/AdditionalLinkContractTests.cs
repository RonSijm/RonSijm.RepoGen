using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AdditionalLinkTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AdditionalLinkContractTests
{
    private static readonly int[] OriginalTargets = [1, 2, 4];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSameDesignerMatchesGeneratedSqliteForCompositeAdditionalReferencesAndSavedResults(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new AdditionalLinkContext(new DbContextOptionsBuilder<AdditionalLinkContext>().UseSqlite(connection).Options);
        await Seed(context);
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.AdditionalLinkOwnerRepositoryOperations")!, context)!;
        var (fake, data) = Repositories(mapped);
        async Task Equal<T>(string operation, params object?[] inputs)
            => JsonSerializer.Serialize(await Invoke<T>(fake, operation, inputs)).Should().Be(JsonSerializer.Serialize(await Invoke<T>(real, operation, inputs)));
        async Task State()
        {
            var rows = await context.Set<AdditionalLinkRow>().AsNoTracking().OrderBy(row => row.Scope).ThenBy(row => row.TargetId)
                .Select(row => new AdditionalLinkData(row.Scope, row.OwnerId, row.TargetId, row.ActorRegion, row.ActorCode, row.SourceId, row.Note, row.Version, row.Hidden)).ToListAsync();
            Rows(data, mapped).Should().Equal(rows); await Equal<AdditionalLinkView?>("FindAsync", new AdditionalLinkRequest(1, 1, []));
        }
        await Equal<CommandResult<AdditionalLinkView>>("ReplaceAsync", new AdditionalLinkRequest(1, 1, [new(1, 1, "us", "b", 2, "changed"), new(1, 3, "us", "b", 2, "new")]));
        await State();
        await Equal<CommandResult<AdditionalLinkView>>("MergeAsync", new AdditionalLinkRequest(1, 1, [new(1, 1, "eu", "a", 1, "stale", 99)], 2));
        await State();
        await Equal<CommandResult>("StageAsync", new AdditionalLinkRequest(1, 1, [new(1, 2, "eu", "a", 1, "staged")], 2));
        await Equal<CommandResult>("StageNameAsync", 2); await State();
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync()); await State();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal<CommandResult<AdditionalLinkView>>("ReplaceAsync", new AdditionalLinkRequest(1, 1, [new(1, 3, "us", "b", 2, "rollback")], 3));
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear(); await State();
        context.Set<AdditionalLinkActor>().Count().Should().Be(2); context.Set<AdditionalLinkSource>().Count().Should().Be(2);
    }

    [Theory]
    [InlineData(true, "actor")]
    [InlineData(false, "actor")]
    [InlineData(true, "source")]
    [InlineData(false, "source")]
    [InlineData(true, "constraint")]
    [InlineData(false, "constraint")]
    public async Task AdditionalForeignKeyAndSaveFailuresRestoreFixupAndRetainEarlierEdits(bool detection, string failure)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new AdditionalLinkContext(new DbContextOptionsBuilder<AdditionalLinkContext>().UseSqlite(connection).Options);
        await Seed(context);
        var owners = await context.Set<AdditionalLinkOwner>().Include(row => row.Links).ToListAsync();
        var actors = await context.Set<AdditionalLinkActor>().Include(row => row.Links).ToListAsync();
        var sources = await context.Set<AdditionalLinkSource>().Include(row => row.Links).ToListAsync();
        var selected = owners.Single(row => row.Scope == 1); var earlier = owners.Single(row => row.Scope == 2);
        earlier.Name = "pending"; context.Entry(earlier).Property(row => row.Name).IsModified = true; context.ChangeTracker.AutoDetectChangesEnabled = detection;
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder().Build());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.AdditionalLinkOwnerRepositoryOperations")!, context)!;
        var request = new AdditionalLinkRequest(1, 1, [new(1, 1, "us", "b", 2, "changed"),
            new(1, 3, failure == "actor" ? "missing" : "us", "b", failure == "source" ? 99 : 2, failure == "constraint" ? "invalid" : "new")]);
        Func<Task> save = async () => await Invoke<CommandResult<AdditionalLinkView>>(repository, "ReplaceAsync", request);
        await save.Should().ThrowAsync<DbUpdateException>(); context.ChangeTracker.DetectChanges();
        selected.Version.Should().Be(1); selected.Links.Select(row => row.TargetId).Should().BeEquivalentTo(OriginalTargets);
        selected.Links.Single(row => row.TargetId == 1).ActorCode.Should().Be("a");
        selected.Links.Single(row => row.TargetId == 1).Note.Should().Be("A");
        actors.SelectMany(row => row.Links).Should().NotContain(row => row.TargetId == 3);
        sources.SelectMany(row => row.Links).Should().NotContain(row => row.TargetId == 3);
        context.ChangeTracker.Entries<AdditionalLinkRow>().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        earlier.Name.Should().Be("pending"); context.Entry(earlier).State.Should().Be(EntityState.Modified);
        (await Invoke<CommandResult<AdditionalLinkView>>(repository, "ReplaceAsync", new AdditionalLinkRequest(1, 1, [new(1, 1, "us", "b", 2, "valid"), new(1, 3, "us", "b", 2, "valid")]))).Status.Should().Be(CommandStatus.Applied);
        context.ChangeTracker.Clear(); (await context.Set<AdditionalLinkOwner>().SingleAsync(row => row.Scope == 2)).Name.Should().Be("pending");
        context.Set<AdditionalLinkActor>().Count().Should().Be(2); context.Set<AdditionalLinkSource>().Count().Should().Be(2);
    }

    private static async Task Seed(AdditionalLinkContext context)
    {
        await context.Database.EnsureCreatedAsync(); context.AddRange(Owners()); context.AddRange(Targets()); context.AddRange(Actors()); context.AddRange(Sources()); context.AddRange(Links());
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
    }
}
