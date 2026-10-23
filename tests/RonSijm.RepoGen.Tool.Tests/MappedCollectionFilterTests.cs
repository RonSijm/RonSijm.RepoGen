using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedCollectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedCollectionFilterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MappedValuesDoNotInferRootOrChildModelFilterWritePermission(bool filters)
    {
        await using var context = new MappedCollectionFilteredContext(new DbContextOptionsBuilder<MappedCollectionContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder(queries: false);
        var parent = builder.Entity<MappedCollectionParent>();
        foreach (var name in new[] { "Replace", "Merge", "Reject", "Stage", "Saved" })
        {
            parent.ExcludeCommand(name);
        }
        // Read permissions do not authorize command filtering, including this read-disabled fixture.
        parent.UseInMemoryModelQueryFilter(includeNavigations: true);
        builder.Entity<MappedCollectionChild>().ExcludeCommand("StageText").UseInMemoryModelQueryFilter(includeNavigations: true);
        parent.HasCommand("Filtered").Where<MappedCollectionRequest>((row, request) => row.Id == request.Id)
            .Patch(setters => setters.SetProperty(row => row.Counter, row => row.Counter + 1))
            .ExpectVersion<int, MappedCollectionRequest>("Revision", request => request.Revision)
            .ReconcileCollection<MappedCollectionChild, MappedCollectionItem, string, MappedCollectionRequest>(row => row.Children, request => request.Items,
                row => row.Code, item => item.Code, children => children.MaximumItems(4)
                    .Update(setters => setters.SetProperty(row => row.Counter, row => row.Counter + 1))
                    .ExpectVersion<int>("Revision", item => item.Revision).MissingItems(RonSijm.RepoGen.Design.RepositoryCollectionRemovalBehavior.Delete))
            .SimulateCollectionWrites(includeModelQueryFilters: filters).UseInMemoryModelQueryFilter(filters).Save().Returns(row => row.Counter);
        if (!filters)
        {
            Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
            generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
            return;
        }
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        var (repository, _, data) = Fixture(ScriptedTestDoubleTestHarness.Compile(bundle), nameof(MappedCollectionFilteredContext));
        var row = ChildStore(data).Find(2)!;
        ChildStore(data).Seed(row with { Audit = 0 }, RonSijm.RepoGen.Testing.InMemoryDuplicateKeyBehavior.Replace);
        var request = Request() with { Items = [Request().Items[0]] };
        var actual = await Call<CommandResult<int>>(repository, "Filtered", request);
        actual.Status.Should().Be(CommandStatus.Applied);
        ChildStore(data).Find(1)!.Revision.Should().Be(4);
        ChildStore(data).Find(2)!.Counter.Should().Be(4, "filtered children are not deletion candidates");
        await MappedCollectionContractTests.SeedAsync(context);
        await context.Database.ExecuteSqlRawAsync("UPDATE \"MappedCollectionChild\" SET \"audit\" = 0 WHERE \"Id\" = 2");
        var production = SavedResultTestHarness.CompileProduction(context, builder.Build());
        var real = Activator.CreateInstance(production.GetType(Namespace + ".Persistence.MappedCollectionParentRepositoryOperations")!, context)!;
        var expected = await Call<CommandResult<int>>(real, "Filtered", request);
        actual.Value.Should().Be(expected.Value);
        var unaffected = await context.Set<MappedCollectionChild>().IgnoreQueryFilters().AsNoTracking().SingleAsync(item => item.Id == 2);
        unaffected.Counter.Should().Be(4);
    }
}
