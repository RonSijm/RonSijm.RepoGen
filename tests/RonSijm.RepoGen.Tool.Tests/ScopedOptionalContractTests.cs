using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ScopedOptionalTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScopedOptionalContractTests
{
    private static readonly int[] OriginalTargets = [1, 2, 6];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActualSqliteAndBothFixturesAgreeOnAbsentPresentAndImmutableScopedReferences(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ScopedOptionalContext(new DbContextOptionsBuilder<ScopedOptionalContext>().UseSqlite(connection).Options);
        await Seed(context); var design = Builder(mapped).Build();
        var assembly = SavedResultTestHarness.CompileProduction(context, design);
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ScopedOptionalOwnerRepositoryOperations")!, context)!;
        var (fake, data) = Repositories(mapped);
        async Task Equal<T>(string operation, params object?[] inputs)
            => JsonSerializer.Serialize(await Invoke<T>(fake, operation, inputs)).Should().Be(JsonSerializer.Serialize(await Invoke<T>(real, operation, inputs)), operation);
        async Task State()
        {
            var rows = (await context.Set<ScopedOptionalRow>().AsNoTracking().OrderBy(row => row.Scope).ThenBy(row => row.TargetId).ToListAsync()).Select(Row);
            Rows(data, mapped).Should().Equal(rows);
            await Equal<ScopedOptionalView?>("FindAsync", new ScopedOptionalRequest(1, 10, 1, []));
            await Equal<List<string>?>("NamesAsync", new ScopedOptionalRequest(1, 10, 1, []));
        }
        await Equal<CommandResult<ScopedOptionalView>>("ReplaceAsync", new ScopedOptionalRequest(1, 10, 1,
            [new(1, 1, 999, "partial ignored", null, null, "keep present"), new(1, 2, 999, "us", "b", 2, "keep absent"),
                new(1, 3, 10, null, null, null, "new absent"), new(1, 4, 10, "us", "b", 2, "new present")]));
        await State();
        foreach (var repository in new[] { fake, real })
        {
            Func<Task> partial = async () => await Invoke<CommandResult>(repository, "StageAsync", new ScopedOptionalRequest(1, 10, 1,
                [new(1, 1, 999, null, null, null, "stale", 99), new(1, 5, 10, "eu", null, null, "partial")], 2));
            await partial.Should().ThrowAsync<ArgumentException>().WithMessage("*independent components*");
            Func<Task> absentOwner = async () => await Invoke<CommandResult>(repository, "StageAsync", new ScopedOptionalRequest(1, 10, 1,
                [new(1, 5, 20, null, null, null, "absent but cross-owner")], 2));
            await absentOwner.Should().ThrowAsync<ArgumentException>().WithMessage("*selected owner*before ownership*");
        }
        await State();
        await Equal<CommandResult>("StageAsync", new ScopedOptionalRequest(1, 10, 1, [new(1, 5, 10, null, null, 2, "origin only")], 2));
        await Equal<CommandResult>("StageNameAsync", 2); await State();
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync()); await State();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal<CommandResult<ScopedOptionalView>>("ReplaceAsync", new ScopedOptionalRequest(1, 10, 1, [new(1, 3, 10, "eu", "a", null, "reviewer only")], 3));
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear(); await State();
        context.Set<ScopedOptionalReviewer>().Count().Should().Be(3); context.Set<ScopedOptionalOrigin>().Count().Should().Be(3);
    }

    [Theory]
    [InlineData(true, "partial")]
    [InlineData(false, "partial")]
    [InlineData(true, "owner")]
    [InlineData(false, "owner")]
    [InlineData(true, "reviewer")]
    [InlineData(false, "reviewer")]
    [InlineData(true, "origin")]
    [InlineData(false, "origin")]
    [InlineData(true, "constraint")]
    [InlineData(false, "constraint")]
    public async Task InvalidCreationAndFailedSavesRestoreSharedOwnershipOptionalFixupAndEarlierEdits(bool detection, string failure)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ScopedOptionalContext(new DbContextOptionsBuilder<ScopedOptionalContext>().UseSqlite(connection).Options);
        await Seed(context);
        var owners = await context.Set<ScopedOptionalOwner>().Include(row => row.Links).ToListAsync();
        await context.Set<ScopedOptionalReviewer>().LoadAsync(); await context.Set<ScopedOptionalOrigin>().LoadAsync();
        var selected = owners.Single(row => row.PartitionId == 1); var earlier = owners.Single(row => row.PartitionId == 2);
        earlier.Name = "pending"; context.Entry(earlier).Property(row => row.Name).IsModified = true; context.ChangeTracker.AutoDetectChangesEnabled = detection;
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder().Build());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ScopedOptionalOwnerRepositoryOperations")!, context)!;
        var item = failure switch
        {
            "partial" => new ScopedOptionalItem(1, 3, 10, "eu", null, null, "partial"),
            "owner" => new ScopedOptionalItem(1, 3, 20, null, null, null, "absent cross-owner"),
            "reviewer" => new ScopedOptionalItem(1, 3, 10, "missing", "missing", null, "missing reviewer"),
            "origin" => new ScopedOptionalItem(1, 3, 10, null, null, 99, "missing origin"),
            _ => new ScopedOptionalItem(1, 3, 10, null, null, null, "invalid")
        };
        Func<Task> fail = async () => await Invoke<CommandResult<ScopedOptionalView>>(repository, "ReplaceAsync", new ScopedOptionalRequest(1, 10, 1,
            [new(1, 1, 999, null, null, null, "changed"), item]));
        if (failure is "partial" or "owner") await fail.Should().ThrowAsync<ArgumentException>();
        else await fail.Should().ThrowAsync<DbUpdateException>();
        context.ChangeTracker.DetectChanges(); selected.Version.Should().Be(1);
        selected.Links.Select(row => row.TargetId).Should().BeEquivalentTo(OriginalTargets);
        selected.Links.Single(row => row.TargetId == 1).Reviewer!.Code.Should().Be("a");
        selected.Links.Single(row => row.TargetId == 2).Reviewer.Should().BeNull();
        selected.Links.Single(row => row.TargetId == 2).Origin.Should().BeNull();
        selected.Links.Should().OnlyContain(row => row.Scope == 1 && row.Zone == 10);
        context.ChangeTracker.Entries<ScopedOptionalRow>().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        earlier.Name.Should().Be("pending"); context.Entry(earlier).State.Should().Be(EntityState.Modified);
        (await Invoke<CommandResult<ScopedOptionalView>>(repository, "ReplaceAsync", new ScopedOptionalRequest(1, 10, 1,
            [new(1, 1, 10, null, null, null, "valid"), new(1, 3, 10, null, null, null, "valid absent")]))).Status.Should().Be(CommandStatus.Applied);
        context.ChangeTracker.Clear(); (await context.Set<ScopedOptionalOwner>().SingleAsync(row => row.PartitionId == 2)).Name.Should().Be("pending");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreationPolicyDoesNotNormalizePreexistingPartialNullReferences(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ScopedOptionalContext(new DbContextOptionsBuilder<ScopedOptionalContext>().UseSqlite(connection).Options);
        await Seed(context, partialSeed: true);
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ScopedOptionalOwnerRepositoryOperations")!, context)!;
        var (fake, data) = Repositories(mapped, partialSeed: true);
        var request = new ScopedOptionalRequest(1, 10, 1, [new(1, 2, 999, "us", "b", 2, "payload only")]);
        JsonSerializer.Serialize(await Invoke<CommandResult<ScopedOptionalView>>(fake, "MergeAsync", request))
            .Should().Be(JsonSerializer.Serialize(await Invoke<CommandResult<ScopedOptionalView>>(real, "MergeAsync", request)));
        var stored = await context.Set<ScopedOptionalRow>().AsNoTracking().SingleAsync(row => row.Scope == 1 && row.TargetId == 2);
        stored.ReviewerRegion.Should().Be("eu"); stored.ReviewerCode.Should().BeNull(); stored.OriginId.Should().BeNull();
        Rows(data, mapped).Single(row => row.Scope == 1 && row.TargetId == 2).Should().Be(Row(stored));
        JsonSerializer.Serialize(await Invoke<List<string>?>(fake, "NamesAsync", request))
            .Should().Be(JsonSerializer.Serialize(await Invoke<List<string>?>(real, "NamesAsync", request)));
    }

    private static async Task Seed(ScopedOptionalContext context, bool partialSeed = false)
    {
        await context.Database.EnsureCreatedAsync(); context.AddRange(Owners()); context.AddRange(Targets()); context.AddRange(Reviewers()); context.AddRange(Origins());
        var links = Links(); if (partialSeed) links.Single(row => row.Scope == 1 && row.TargetId == 2).ReviewerRegion = "eu";
        context.AddRange(links); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
    }
}
