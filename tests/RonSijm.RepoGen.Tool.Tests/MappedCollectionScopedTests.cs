using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedCollectionScopedTests
{
    [Fact]
    public async Task OnlyTheChildNeedsMappedStateWhenTheParentIsAnOrdinaryPublicStore()
    {
        using var context = new MappedPropertyCollectionContext(new DbContextOptionsBuilder<MappedPropertyCollectionContext>().UseSqlite("Data Source=:memory:").Options);
        const string scope = "Generated.ChildOnly";
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(scope + ".Contracts").UseGeneratedNamespace(scope + ".Persistence")
            .UseScriptedTestDoubles(scope + ".Testing").UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands());
        var parent = builder.Entity<MappedPropertyOwner>();
        parent.HasInMemoryStore(row => row.Id, row => new MappedPropertyOwner { Id = row.Id, Title = row.Title });
        parent.HasCommand("Change").Where<MappedPropertyCollectionInput>((row, input) => row.Id == input.Id)
            .Patch(setters => setters.SetProperty(row => row.Title, "changed"))
            .ReconcileCollection<MappedPropertyChild, MappedPropertyItem, int, MappedPropertyCollectionInput>(row => row.Children, input => input.Items,
                row => row.Id, item => item.Id, children => children.MaximumItems(4)
                    .Update(setters => setters.SetProperty(row => row.Title, (row, item) => item.Title)
                        .SetProperty(row => row.Counter, row => row.Counter + 1))
                    .ExpectVersion<int>(MappedPropertyContext.Token, item => item.Version))
            .SimulateCollectionWrites().Save();
        builder.Entity<MappedPropertyChild>().HasInMemoryStore<MappedCollectionChildSeed, int>(row => row.Id,
                row => new(row.Id, row.ParentId, row.Code, row.Text, row.Counter, row.Category, row.Audit, row.Revision,
                    row.OptionalVersion, row.Stamp == null ? null : row.Stamp.ToArray(), row.Extra))
            .MapInMemoryEntity<MappedCollectionChildSeed>(row => new MappedPropertyChild { Id = row.Id, OwnerId = row.ParentId, Title = row.Text, Counter = row.Counter })
            .MapInMemoryProperty<MappedCollectionChildSeed, int>(MappedPropertyContext.Token, row => row.Revision)
            .MapInMemoryWrites<MappedCollectionChildSeed>((row, saved) => new(row.Id, row.OwnerId, "unused", row.Title, row.Counter, "unused", 0,
                EF.Property<int>(row, MappedPropertyContext.Token), null, null), useMappedPropertyState: true)
            .HasInMemoryGeneratedValue<int>(MappedPropertyContext.Token, (row, rows) => row.Counter + 1)
            .HasVisibilityPolicy("PositiveVersion", row => EF.Property<int>(row, MappedPropertyContext.Token) > 0);
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType(scope + ".Testing.MappedPropertyCollectionContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        ((InMemoryStore<MappedPropertyOwner, int>)data.GetType().GetProperty("MappedPropertyOwnerStore")!.GetValue(data)!).Seed(new() { Id = 1 });
        var store = (InMemoryStore<MappedCollectionChildSeed, int>)data.GetType().GetProperty("MappedPropertyChildStore")!.GetValue(data)!;
        store.Seed(new(1, 1, "unused", "old", 0, "unused", 0, 1, null, null));
        var repository = Activator.CreateInstance(assembly.GetType(scope + ".Testing.MappedPropertyOwnerRepositoryTestDouble")!, fixture)!;
        var result = await MappedStateTestHarness.Call<CommandResult>(repository, "ChangeAsync", new MappedPropertyCollectionInput(1, [new(1, "changed", 1)]));
        result.Status.Should().Be(CommandStatus.Applied);
        store.Find(1)!.Revision.Should().Be(2);
        store.Find(1)!.Text.Should().Be("changed");
    }
}
