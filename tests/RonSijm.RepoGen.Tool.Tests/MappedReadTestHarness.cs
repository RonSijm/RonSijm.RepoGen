using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class MappedReadTestHarness
{
    internal const string Namespace = "Generated.MappedReads";
    private static readonly Lazy<Assembly> Generated = new(() =>
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder().Build()));
    });
    internal static MappedReadContext Context() => new(new DbContextOptionsBuilder<MappedReadContext>().UseSqlite("Data Source=:memory:").Options);

    internal static RepositoryModelBuilder Base(bool mappings = true, bool strict = true, bool queries = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries(queries).RequirePortableTestQueries(strict && queries));
        var item = builder.Entity<MappedReadItem>();
        item.WriteEnabled = false;
        item.HasInMemoryStore<MappedReadSeed, int>(row => row.Id, row => new(row.Id, row.Name, row.Caption, row.Rank, row.Audit, row.Archived,
                row.State, row.Fingerprint == null ? null : row.Fingerprint.ToArray(), row.Tag, row.FolderId, row.FixedValue))
            .MapInMemoryEntity<MappedReadSeed>(row => new MappedReadItem { Id = row.Id, FolderId = row.FolderId });
        item.UseInMemoryModelQueryFilter(includeNavigations: true).HasInMemoryNavigation(row => row.Folder);
        item.HasVisibilityPolicy("Visible", row => row.Name != "hidden");
        if (mappings)
        {
            item.MapInMemoryProperty<MappedReadSeed, string>(nameof(MappedReadItem.Name), row => row.Name)
                .MapInMemoryProperty<MappedReadSeed, string>(nameof(MappedReadItem.Caption), row => row.Caption)
                .MapInMemoryProperty<MappedReadSeed, int>(nameof(MappedReadItem.PublicRank), row => row.Rank)
                .MapInMemoryProperty<MappedReadSeed, int>("audit", row => row.Audit)
                .MapInMemoryProperty<MappedReadSeed, bool>("archived", row => row.Archived)
                .MapInMemoryProperty<MappedReadSeed, MappedReadState?>("state", row => row.State)
                .MapInMemoryProperty<MappedReadSeed, byte[]?>("fingerprint", row => row.Fingerprint)
                .MapInMemoryProperty<MappedReadSeed, int>("fixedValue", row => row.FixedValue)
                .MapInMemoryProperty<MappedReadSeed, string>(MappedReadContext.Tag, row => row.Tag);
        }
        var folder = builder.Entity<MappedReadFolder>();
        folder.WriteEnabled = false;
        folder.HasInMemoryStore<MappedReadFolderSeed, int>(row => row.Id, row => new(row.Id, row.Name, row.Hidden))
            .MapInMemoryEntity<MappedReadFolderSeed>(row => new MappedReadFolder { Id = row.Id });
        folder.UseInMemoryModelQueryFilter(includeNavigations: true).HasInMemoryNavigation(row => row.Items);
        if (mappings)
        {
            folder.MapInMemoryProperty<MappedReadFolderSeed, string>(nameof(MappedReadFolder.Name), row => row.Name)
                .MapInMemoryProperty<MappedReadFolderSeed, bool>("hidden", row => row.Hidden);
        }
        return builder;
    }

    internal static RepositoryModelBuilder Builder(bool mappings = true, bool strict = true, bool queries = true)
    {
        var builder = Base(mappings, strict, queries);
        var item = builder.Entity<MappedReadItem>();
        item.HasDefaultSort(row => row.Caption);
        item.HasDefaultSort(row => row.Id);
        item.HasSortableFields(row => row.Name, row => row.Caption);
        item.HasProjection<MappedReadCard>(row => new MappedReadCard(row.Id, row.Name, row.Caption, row.PublicRank, EF.Property<int>(row, "audit"),
            EF.Property<MappedReadState?>(row, "state"), EF.Property<byte[]?>(row, "fingerprint"), (string)row[MappedReadContext.Tag]!, EF.Property<int>(row, "fixedValue")));
        item.HasQuery("Cards").ProjectTo<MappedReadCard>().List();
        item.HasQuery("Page").ProjectTo<MappedReadCard>().Paged();
        item.HasQuery("Stream").ProjectTo<MappedReadCard>().Stream();
        item.HasQuery("Get").Where<int>((row, id) => row.Id == id).ProjectTo<MappedReadCard>().SingleOrDefault();
        item.HasQuery("All").IgnoreQueryFilters().ProjectTo<MappedReadCard>().List();
        item.HasQuery("IncludingHidden").BypassVisibility("Visible").ProjectTo<MappedReadCard>().List();
        item.HasQuery("ByRank").Where<int>((row, rank) => row.PublicRank >= rank).Select(row => row.Id).List();
        item.HasQuery("Count").Where(row => EF.Property<int>(row, "audit") > 0).Count();
        item.HasQuery("Names").Select(row => EF.Property<string>(row, nameof(MappedReadItem.Name))).List();
        item.HasQuery("Tags").Select(row => EF.Property<string>(row, MappedReadContext.Tag)).List();
        item.HasQuery("Sorted").AllowSorting("field", "descending", row => row.Name, row => row.Caption).ProjectTo<MappedReadCard>().List();
        item.HasQuery("Totals").GroupBy(row => (string)row[MappedReadContext.Tag]!,
                group => new MappedReadTotal { Tag = group.Key, Count = group.Count(), Total = group.Sum(row => row.PublicRank) })
            .Results<MappedReadTotal>().OrderBy(row => row.Tag).List();
        item.HasQuery("Source").From(sources => sources.Set<MappedReadItem>().OrderBy(row => row.Id)
            .Select(row => new MappedReadCard(row.Id, row.Name, row.Caption, row.PublicRank, EF.Property<int>(row, "audit"),
                EF.Property<MappedReadState?>(row, "state"), EF.Property<byte[]?>(row, "fingerprint"), (string)row[MappedReadContext.Tag]!, EF.Property<int>(row, "fixedValue")))).List();
        item.HasQuery("FolderNames").OrderBy(row => row.Id).Select(row => row.Folder == null ? "none" : row.Folder.Name).List();
        builder.Entity<MappedReadFolder>().HasQuery("Captions").OrderBy(row => row.Id)
            .Select(row => row.Items.OrderBy(child => child.Id).Select(child => child.Caption).ToList()).List();
        return builder;
    }

    internal static MappedReadSeed[] Seeds() =>
    [
        new(1, "one", "alpha", 20, 2, false, MappedReadState.Draft, [1, 2], "a", 1),
        new(2, "two", "beta", 10, 3, false, null, null, "a", 1),
        new(3, "deleted", "archive", 5, 4, true, MappedReadState.Published, [3], "b", 1),
        new(4, "hidden", "hidden", 40, 5, false, MappedReadState.Published, [4], "b", 1),
        new(5, "other folder", "gamma", 30, 6, false, MappedReadState.Published, [5], "c", 2)
    ];
    internal static MappedReadFolderSeed[] Folders() => [new(1, "open", false), new(2, "closed", true)];

    internal static (object Items, object Folders, InMemoryDataContext Data) Fixture(Assembly? assembly = null)
    {
        assembly ??= Generated.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MappedReadContextRepositoryFixture")!)!;
        var data = (InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        ModelFilterTestHarness.Store<MappedReadSeed>(data, nameof(MappedReadItem)).SeedRange(Seeds());
        ModelFilterTestHarness.Store<MappedReadFolderSeed>(data, nameof(MappedReadFolder)).SeedRange(Folders());
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MappedReadItemRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MappedReadFolderRepositoryTestDouble")!, fixture)!, data);
    }
}
