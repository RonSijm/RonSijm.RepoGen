using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class MappedKeyTestHarness
{
    internal const string Namespace = "Generated.MappedKeys";
    private static readonly Lazy<Assembly> Generated = new(() =>
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder().Build()));
    });
    internal static MappedKeyContext Context() => new(new DbContextOptionsBuilder<MappedKeyContext>().UseSqlite("Data Source=:memory:").Options);

    internal static RepositoryModelBuilder Base(bool permission = true, bool strict = true, bool queries = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries(queries).RequirePortableTestQueries(strict && queries));
        var owner = builder.Entity<MappedKeyOwner>();
        owner.WriteEnabled = false;
        owner.HasInMemoryStore<MappedKeyOwnerSeed, int>(row => row.Id, row => new(row.Id, row.Scope, row.Code, row.Name, row.Archived))
            .MapInMemoryEntity<MappedKeyOwnerSeed>(row => new MappedKeyOwner { Id = row.Id, Name = row.Name })
            .MapInMemoryProperty<MappedKeyOwnerSeed, int>("Scope", row => row.Scope)
            .MapInMemoryProperty<MappedKeyOwnerSeed, string>(nameof(MappedKeyOwner.Code), row => row.Code)
            .MapInMemoryProperty<MappedKeyOwnerSeed, bool>("Archived", row => row.Archived);
        owner.HasInMemoryNavigation(row => row.Children, useMappedPropertyKeys: permission)
            .HasInMemoryNavigation(row => row.Tags, useMappedPropertyKeys: permission).UseInMemoryModelQueryFilter(includeNavigations: true);
        owner.HasVisibilityPolicy("Visible", row => row.Name != "private");
        var child = builder.Entity<MappedKeyChild>();
        child.WriteEnabled = false;
        child.HasInMemoryStore<MappedKeyChildSeed, int>(row => row.Id, row => new(row.Id, row.OwnerScope, row.OwnerCode, row.Name, row.Deleted))
            .MapInMemoryEntity<MappedKeyChildSeed>(row => new MappedKeyChild { Id = row.Id, Name = row.Name, Deleted = row.Deleted })
            .MapInMemoryProperty<MappedKeyChildSeed, int?>("OwnerScope", row => row.OwnerScope)
            .MapInMemoryProperty<MappedKeyChildSeed, string?>("OwnerCode", row => row.OwnerCode);
        child.HasInMemoryNavigation(row => row.Owner, useMappedPropertyKeys: permission).UseInMemoryModelQueryFilter(includeNavigations: true);
        var tag = builder.Entity<MappedKeyTag>();
        tag.WriteEnabled = false;
        tag.HasInMemoryStore<MappedKeyTagSeed, int>(row => row.Id, row => new(row.Id, row.Name))
            .MapInMemoryEntity<MappedKeyTagSeed>(row => new MappedKeyTag { Name = row.Name })
            .MapInMemoryProperty<MappedKeyTagSeed, int>(nameof(MappedKeyTag.Id), row => row.Id);
        tag.HasInMemoryNavigation(row => row.Owners, useMappedPropertyKeys: permission);
        var link = builder.Entity<MappedKeyLink>();
        link.WriteEnabled = false;
        link.HasInMemoryStore<MappedKeyLinkSeed, int>(row => row.Id, row => new(row.Id, row.OwnerScope, row.OwnerCode, row.TagId, row.Removed))
            .MapInMemoryEntity<MappedKeyLinkSeed>(row => new MappedKeyLink { Id = row.Id })
            .MapInMemoryProperty<MappedKeyLinkSeed, int?>("OwnerScope", row => row.OwnerScope)
            .MapInMemoryProperty<MappedKeyLinkSeed, string?>("ownerCode", row => row.OwnerCode)
            .MapInMemoryProperty<MappedKeyLinkSeed, int?>("TagId", row => row.TagId)
            .MapInMemoryProperty<MappedKeyLinkSeed, bool>("Removed", row => row.Removed);
        link.UseInMemoryModelQueryFilter(includeNavigations: true);
        return builder;
    }

    internal static RepositoryModelBuilder Builder(bool permission = true, bool strict = true, bool queries = true)
    {
        var builder = Base(permission, strict, queries);
        var owner = builder.Entity<MappedKeyOwner>();
        owner.HasProjection<MappedKeyCard>(row => new MappedKeyCard(row.Id, row.Name,
            row.Children.OrderBy(child => child.Id).Select(child => child.Name).ToList(),
            row.Tags.Where(tag => tag != null).OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList()));
        owner.HasQuery("Cards").OrderBy(row => row.Id).ProjectTo<MappedKeyCard>().List();
        owner.HasQuery("Page").OrderBy(row => row.Id).ProjectTo<MappedKeyCard>().Paged();
        owner.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<MappedKeyCard>().Stream();
        owner.HasQuery("All").IgnoreQueryFilters().OrderBy(row => row.Id).ProjectTo<MappedKeyCard>().List();
        owner.HasQuery("Source").From(sources => sources.Set<MappedKeyOwner>().OrderBy(row => row.Id)
            .Select(row => row.Children.Count)).List();
        owner.HasQuery("HasTag").Where<string>((row, name) => row.Tags.Where(tag => tag != null).Any(tag => tag.Name == name))
            .OrderBy(row => row.Id).Select(row => row.Id).List();
        builder.Entity<MappedKeyChild>().HasQuery("Cards").OrderBy(row => row.Id)
            .Select(row => new MappedKeyChildCard(row.Id, row.Name, row.Owner == null ? "none" : row.Owner.Name)).List();
        builder.Entity<MappedKeyTag>().HasQuery("Owners").OrderBy(row => row.Id)
            .Select(row => row.Owners.Where(owner => owner != null).OrderBy(owner => owner.Id).Select(owner => owner.Name).ToList()).List();
        return builder;
    }

    internal static MappedKeyOwnerSeed[] Owners() => [new(1, 7, "a", "one"), new(2, 8, "a", "two"), new(3, 7, "b", "archived", true), new(4, 7, "p", "private")];
    internal static MappedKeyChildSeed[] Children() => [new(1, 7, "a", "first"), new(2, 8, "a", "second"), new(3, 7, "b", "filtered owner"),
        new(4, null, "a", "partial"), new(5, 7, "a", "deleted", true)];
    internal static MappedKeyTagSeed[] Tags() => [new(1, "red"), new(2, "blue")];
    internal static MappedKeyLinkSeed[] Links() => [new(1, 7, "a", 2), new(2, 8, "a", 1), new(3, 7, "b", 1), new(4, 7, "p", 1),
        new(5, 7, "a", null), new(6, null, "a", 1), new(7, 7, "a", 1, true)];

    internal static (object Owner, object Child, object Tag, InMemoryDataContext Data) Fixture(Assembly? assembly = null)
    {
        assembly ??= Generated.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MappedKeyContextRepositoryFixture")!)!;
        var data = (InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        Store<MappedKeyOwnerSeed>(data, nameof(MappedKeyOwner)).SeedRange(Owners());
        Store<MappedKeyChildSeed>(data, nameof(MappedKeyChild)).SeedRange(Children());
        Store<MappedKeyTagSeed>(data, nameof(MappedKeyTag)).SeedRange(Tags());
        Store<MappedKeyLinkSeed>(data, nameof(MappedKeyLink)).SeedRange(Links());
        object Repository(string name) => Activator.CreateInstance(assembly.GetType(Namespace + ".Testing." + name + "RepositoryTestDouble")!, fixture)!;
        return (Repository(nameof(MappedKeyOwner)), Repository(nameof(MappedKeyChild)), Repository(nameof(MappedKeyTag)), data);
    }

    internal static InMemoryStore<T, int> Store<T>(InMemoryDataContext data, string name) where T : class => ModelFilterTestHarness.Store<T>(data, name);
}
