using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.LinkWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredCollectionLinkTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitLinkFiltersDoNotActAsEndpointConstraintsAndSavedDtoFiltersRemainIndependent(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new FilteredLinkCollectionContext(new DbContextOptionsBuilder<LinkWriteContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Owners()); context.AddRange(TargetRows()); context.AddRange(LinkRows());
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var builder = LinkBuilder(mapped); var bundle = RepositoryGenerator.Generate(context, builder.Build());
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle); var realAssembly = SavedResultTestHarness.CompileProduction(context, builder.Build());
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.FilteredLinkCollectionContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped)
        {
            OwnerStore(data).SeedRange(Owners().Select(row => new LinkWriteOwnerData(row.TenantId, row.Id, row.Name, row.Version, row.Hidden)));
            TargetStore(data).SeedRange(TargetRows().Select(row => new LinkWriteTargetData(row.Region, row.Code, row.Name, row.Hidden)));
            LinkStore(data).SeedRange(LinkRows().Select(row => new LinkWriteRowData(row.OwnerTenantId, row.OwnerId, row.TargetRegion, row.TargetCode, row.Note, row.Version, row.Hidden)));
        }
        else
        {
            Store<LinkWriteOwner, (int, int)>(data, nameof(LinkWriteOwner)).SeedRange(Owners());
            Store<LinkWriteTarget, (string, string)>(data, nameof(LinkWriteTarget)).SeedRange(TargetRows());
            Store<LinkWriteRow, (int, int, string, string)>(data, nameof(LinkWriteRow)).SeedRange(LinkRows());
        }
        var fake = Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.LinkWriteOwnerRepositoryTestDouble")!, fixture)!;
        var real = Activator.CreateInstance(realAssembly.GetType(Namespace + ".Persistence.LinkWriteOwnerRepositoryOperations")!, context)!;
        var input = new LinkWriteRequest(1, 1, [new("eu", "a", "updated"), new("eu", "c", "created")]);
        var result = await FilteredCollectionTestHarness.Invoke<Task<CommandResult<LinkWriteView>>>(fake, "Apply", input);
        var sql = await FilteredCollectionTestHarness.Invoke<Task<CommandResult<LinkWriteView>>>(real, "Apply", input);
        JsonSerializer.Serialize(result).Should().Be(JsonSerializer.Serialize(sql)); result.Value!.Tags.Should().Equal("A");
        var rows = Rows(data, mapped); rows.Should().HaveCount(5);
        rows.Single(row => row.OwnerTenantId == 1 && row.TargetCode == "b").Note.Should().Be("deleted");
        rows.Single(row => row.OwnerTenantId == 1 && row.TargetCode == "h").Hidden.Should().BeTrue();
        rows.Single(row => row.OwnerTenantId == 1 && row.TargetCode == "c").Note.Should().Be("created");
        context.ChangeTracker.Clear();
        (await context.Set<LinkWriteRow>().IgnoreQueryFilters().CountAsync()).Should().Be(5);
        (await context.Set<LinkWriteTarget>().IgnoreQueryFilters().CountAsync()).Should().Be(5);
    }

    [Fact]
    public void LinkReadAndSavedGraphPermissionsDoNotGrantLinkMutationFilterConsent()
    {
        using var context = new FilteredLinkCollectionContext(new DbContextOptionsBuilder<LinkWriteContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = LinkBuilder(true, filters: false);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Active EF filters*");
    }

    private static RepositoryModelBuilder LinkBuilder(bool mapped, bool filters = true)
    {
        var builder = Builder(mapped); var owner = builder.Entity<LinkWriteOwner>();
        foreach (var command in builder.Build().Entities.Single(entity => entity.EntityType == typeof(LinkWriteOwner)).Commands.ToArray())
            owner.ExcludeCommand(command.MethodName);
        builder.Entity<LinkWriteRow>().UseInMemoryModelQueryFilter(includeNavigations: true);
        builder.Entity<LinkWriteTarget>().UseInMemoryModelQueryFilter(includeNavigations: true);
        owner.HasCommand("Apply").Where<LinkWriteRequest>((row, input) => row.TenantId == input.TenantId && row.Id == input.OwnerId)
            .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
            .ExpectVersion<int, LinkWriteRequest>(row => row.Version, (row, input) => input.ExpectedVersion)
            .ReconcileLinks<LinkWriteRow, LinkWriteItem, (string, string), LinkWriteRequest>(row => row.Links, input => input.Items!,
                row => new ValueTuple<string, string>(row.TargetRegion, row.TargetCode), item => new ValueTuple<string, string>(item.Region, item.Code),
                collection => collection.MaximumItems(4).Add(item => new LinkWriteRow { TargetRegion = item.Region, TargetCode = item.Code, Note = item.Note, Version = 1, Hidden = false })
                    .Update(setters => setters.SetProperty(row => row.Note, (row, item) => item.Note)).AllowWrite(row => row.Version)
                    .ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                    .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
            .UseInMemoryModelQueryFilter(includeNavigations: true).SimulateCollectionWrites(includeModelQueryFilters: filters).Save()
            .ReturnsSaved(row => new LinkWriteView(row.TenantId, row.Id, row.Version, row.Tags.OrderBy(target => target.Code).Select(target => target.Name).ToList()));
        return builder;
    }
    private static LinkWriteRow[] LinkRows() => Links().Select(row => { if (row.TargetCode == "b") row.Note = "deleted"; return row; }).ToArray();
    private static LinkWriteTarget[] TargetRows() => [.. Targets(), new() { Region = "eu", Code = "c", Name = "Filtered target", Hidden = true }];
}
