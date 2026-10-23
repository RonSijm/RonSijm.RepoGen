using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class LinkWriteGeneratedKeyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedGeneratedJoinKeysCannotLeavePhantomLinksOrPreventARetry(bool automaticDetection)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new LinkWriteGeneratedContext(new DbContextOptionsBuilder<LinkWriteContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(LinkWriteTestHarness.Owners()); context.AddRange(LinkWriteTestHarness.Targets()); context.AddRange(LinkWriteTestHarness.Links());
        context.Add(new LinkWriteTarget { Region = "us", Code = "b", Name = "US B" });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var owner = await context.Set<LinkWriteOwner>().Include(row => row.Links).Include(row => row.Tags).SingleAsync(row => row.TenantId == 1);
        var targets = await context.Set<LinkWriteTarget>().Include(row => row.Links).Include(row => row.Owners).ToListAsync();
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(LinkWriteTestHarness.Namespace + ".Contracts").UseGeneratedNamespace(LinkWriteTestHarness.Namespace + ".Persistence"));
        builder.Entity<LinkWriteOwner>().HasCommand("Change").Where<LinkWriteRequest>((row, request) => row.Id == request.OwnerId && row.TenantId == request.TenantId)
            .ReconcileLinks<LinkWriteRow, LinkWriteItem, (string, string), LinkWriteRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<string, string>(row.TargetRegion, row.TargetCode), item => new ValueTuple<string, string>(item.Region, item.Code),
                links => links.MaximumItems(4).Add(item => new LinkWriteRow { TargetRegion = item.Region, TargetCode = item.Code, Note = item.Note })
                    .MissingItems(RepositoryCollectionRemovalBehavior.Delete)).Save();
        var assembly = SavedResultTestHarness.CompileProduction(context, builder.Build());
        var repository = Activator.CreateInstance(assembly.GetType(LinkWriteTestHarness.Namespace + ".Persistence.LinkWriteOwnerRepositoryOperations")!, context)!;
        Func<Task> fail = async () => await Invoke<CommandResult>(repository, "ChangeAsync", new LinkWriteRequest(1, 1, [new("us", "a", "valid"), new("us", "b", "invalid")]));
        await fail.Should().ThrowAsync<DbUpdateException>();
        context.ChangeTracker.DetectChanges();
        owner.Links.Should().HaveCount(3); owner.Tags.Should().HaveCount(3);
        targets.Where(row => row.Region == "us").Should().OnlyContain(row => row.Links.Count == 0 && row.Owners.Count == 0);
        context.ChangeTracker.Entries<LinkWriteRow>().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        (await context.FindAsync<LinkWriteRow>(5)).Should().BeNull("failed generated values must not remain in EF's identity map");
        (await Invoke<CommandResult>(repository, "ChangeAsync", new LinkWriteRequest(1, 1, [new("us", "a", "valid"), new("us", "b", "valid")]))).Status.Should().Be(CommandStatus.Applied);
        context.ChangeTracker.Clear(); context.Set<LinkWriteRow>().Count().Should().Be(3);
    }
}
