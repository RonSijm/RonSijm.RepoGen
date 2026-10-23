using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryCollectionCompoundKeyTests
{
    [Fact]
    public async Task CompleteCompositeOwnershipIsUsedForSelectionAndEveryCreatedForeignKeyComponent()
    {
        using var context = new MemoryCompoundCollectionContext(new DbContextOptionsBuilder<MemoryCompoundCollectionContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.CompoundCollection.Contracts")
            .UseScriptedTestDoubles("Generated.CompoundCollection.TestDoubles").UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        var parent = builder.Entity<MemoryCompoundParent>();
        parent.HasInMemoryStore(row => new ValueTuple<int, int>(row.Id, row.Region), row => new MemoryCompoundParent { Id = row.Id, Region = row.Region });
        parent.HasCommand("Replace").Where<MemoryCollectionRequest>((row, request) => row.Id == request.Id && row.Region == request.OwnerId)
            .ReconcileCollection<MemoryCompoundChild, MemoryCollectionItem, string, MemoryCollectionRequest>(row => row.Children, request => request.Items!, row => row.Code, item => item.Key,
                children => children.MaximumItems(3).Add(item => new MemoryCompoundChild { Code = item.Key, Text = item.Text })
                    .Update(setters => setters.SetProperty(row => row.Text, (row, item) => item.Text)).MissingItems(RepositoryCollectionRemovalBehavior.Delete))
            .Save().SimulateCollectionWrites();
        builder.Entity<MemoryCompoundChild>().HasInMemoryStore(row => row.Id,
                row => new MemoryCompoundChild { Id = row.Id, ParentId = row.ParentId, Region = row.Region, Code = row.Code, Text = row.Text })
            .HasInMemoryGeneratedValue(row => row.Id, (row, existing) => existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1);
        var bundle = RepositoryGenerator.Generate(context, builder.Build()); var assembly = ScriptedTestDoubleTestHarness.Compile(bundle);
        const string ns = "Generated.CompoundCollection.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "MemoryCompoundCollectionContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType(ns + "MemoryCompoundParentRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var parents = (InMemoryStore<MemoryCompoundParent, (int, int)>)data.GetType().GetProperty("MemoryCompoundParentStore")!.GetValue(data)!;
        var children = (InMemoryStore<MemoryCompoundChild, int>)data.GetType().GetProperty("MemoryCompoundChildStore")!.GetValue(data)!;
        parents.SeedRange([new() { Id = 1, Region = 10 }, new() { Id = 1, Region = 20 }]);
        children.SeedRange([new() { Id = 1, ParentId = 1, Region = 10, Code = "a", Text = "old" }, new() { Id = 2, ParentId = 1, Region = 10, Code = "b" },
            new() { Id = 3, ParentId = 1, Region = 20, Code = "a", Text = "other region" }]);
        var result = await MemoryWriteTestHarness.Invoke<CommandResult>(repository, "ReplaceAsync", new MemoryCollectionRequest(1, 10, "", [new("a", "new"), new("c", "created")]));
        result.Status.Should().Be(CommandStatus.Applied); result.AffectedRows.Should().Be(3);
        children.Find(1)!.Text.Should().Be("new"); children.Find(2).Should().BeNull(); children.Find(3)!.Text.Should().Be("other region");
        children.Find(4)!.ParentId.Should().Be(1); children.Find(4)!.Region.Should().Be(10);
        data.PendingCount.Should().Be(0);
    }
}
