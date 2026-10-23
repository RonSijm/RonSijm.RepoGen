using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class ShadowReadTestHarness
{
    internal const string Namespace = "Generated.ShadowReads";
    private static readonly Lazy<Assembly> Generated = new(() =>
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder().Build()));
    });

    internal static ShadowReadContext Context() => new(new DbContextOptionsBuilder<ShadowReadContext>().UseSqlite("Data Source=:memory:").Options);

    internal static RepositoryModelBuilder Base(bool bindings = true, bool filters = true, bool strict = true, bool queries = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries(queries).RequirePortableTestQueries(strict && queries));
        var note = builder.Entity<ShadowReadNote>();
        note.WriteEnabled = false;
        note.HasInMemoryStore<ShadowReadSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Hidden, row.FolderId, row.Rank, row.Code,
            row.Score, row.Deleted, row.Status, row.Fingerprint == null ? null : row.Fingerprint.ToArray()))
            .MapInMemoryEntity<ShadowReadSeed>(row => new ShadowReadNote { Id = row.Id, Title = row.Title, Hidden = row.Hidden, FolderId = row.FolderId, __repoGenProperty0 = row.Id });
        note.HasInMemoryNavigation(row => row.Folder).UseInMemoryModelQueryFilter(filters, includeNavigations: true);
        note.HasVisibilityPolicy("Visible", row => !row.Hidden);
        if (bindings)
        {
            note.MapInMemoryProperty<ShadowReadSeed, int>("Rank", row => row.Rank)
                .MapInMemoryProperty<ShadowReadSeed, string>("Code", row => row.Code)
                .MapInMemoryProperty<ShadowReadSeed, int?>("Score", row => row.Score)
                .MapInMemoryProperty<ShadowReadSeed, bool>("Deleted", row => row.Deleted)
                .MapInMemoryProperty<ShadowReadSeed, ShadowReadStatus>("Status", row => row.Status)
                .MapInMemoryProperty<ShadowReadSeed, byte[]?>("Fingerprint", row => row.Fingerprint);
        }
        var folder = builder.Entity<ShadowReadFolder>();
        folder.WriteEnabled = false;
        folder.HasInMemoryStore<ShadowReadFolderSeed, int>(row => row.Id, row => new(row.Id, row.Name, row.Archived))
            .MapInMemoryEntity<ShadowReadFolderSeed>(row => new ShadowReadFolder { Id = row.Id, Name = row.Name });
        folder.HasInMemoryNavigation(row => row.Notes).UseInMemoryModelQueryFilter(filters, includeNavigations: true);
        if (bindings)
        {
            folder.MapInMemoryProperty<ShadowReadFolderSeed, bool>("Archived", row => row.Archived);
        }
        return builder;
    }

    internal static RepositoryModelBuilder Builder(bool bindings = true, bool strict = true, bool queries = true)
    {
        var builder = Base(bindings, strict: strict, queries: queries);
        var note = builder.Entity<ShadowReadNote>();
        note.HasProjection<ShadowReadCard>(row => new ShadowReadCard(row.Id, row.Title, EF.Property<int>(row, "Rank"), EF.Property<string>(row, "Code"),
            EF.Property<int?>(row, "Score"), EF.Property<ShadowReadStatus>(row, "Status"), EF.Property<byte[]?>(row, "Fingerprint")));
        note.HasDefaultSort(row => EF.Property<int>(row, "Rank"));
        note.HasQuery("Cards").ProjectTo<ShadowReadCard>().List();
        note.HasQuery("Page").ProjectTo<ShadowReadCard>().Paged();
        note.HasQuery("Stream").ProjectTo<ShadowReadCard>().Stream();
        note.HasQuery("Get").Where<int>((row, id) => row.Id == id).ProjectTo<ShadowReadCard>().SingleOrDefault();
        note.HasQuery("First").ProjectTo<ShadowReadCard>().FirstOrDefault();
        note.HasQuery("Ranked").Where<int>((row, rank) => EF.Property<int>(row, "Rank") >= rank).Select(row => row.Id).List();
        note.HasQuery("Count").Where(row => EF.Property<int>(row, "Rank") > 0).Count();
        note.HasQuery("Exists").Where<string>((row, code) => EF.Property<string>(row, "Code") == code).Exists();
        note.HasQuery("Maximum").From(sources => sources.Set<ShadowReadNote>().Select(row => EF.Property<int>(row, "Rank"))).Max(rank => rank);
        note.HasQuery("Groups").GroupBy(row => EF.Property<string>(row, "Code"),
            group => new ShadowReadGroup { Code = group.Key, Count = group.Count(), Total = group.Sum(row => EF.Property<int>(row, "Rank")) })
            .Results<ShadowReadGroup>().OrderBy(row => row.Code).List();
        note.HasQuery("Lookup").Select(row => new { row.Id, Rank = EF.Property<int>(row, "Rank") }).Dictionary(row => row.Id, row => row.Rank);
        note.HasQuery("All").IgnoreQueryFilters().ProjectTo<ShadowReadCard>().List();
        note.HasQuery("IncludingHidden").BypassVisibility("Visible").ProjectTo<ShadowReadCard>().List();
        note.HasQuery("Join").From(sources => sources.Set<ShadowReadNote>().Join(sources.Set<ShadowReadFolder>(), row => row.FolderId, folder => (int?)folder.Id,
                (row, folder) => new { row, folder }).OrderBy(pair => pair.row.Id)
            .Select(pair => new ShadowReadCard(pair.row.Id, pair.folder.Name, EF.Property<int>(pair.row, "Rank"), EF.Property<string>(pair.row, "Code"),
                EF.Property<int?>(pair.row, "Score"), EF.Property<ShadowReadStatus>(pair.row, "Status"), EF.Property<byte[]?>(pair.row, "Fingerprint"))))
            .List();
        note.HasQuery("Folders").OrderBy(row => row.Id).Select(row => row.Folder == null ? "none" : row.Folder.Name).List();
        // The shadow value on a locally guarded related row is safe, without inferring SQL null propagation.
        note.HasQuery("ArchiveFlags").OrderBy(row => row.Id).Select(row => row.Folder == null ? (bool?)null : EF.Property<bool>(row.Folder, "Archived")).List();
        builder.Entity<ShadowReadFolder>().HasQuery("Ranks").OrderBy(row => row.Id)
            .Select(row => row.Notes.OrderBy(child => child.Id).Select(child => EF.Property<int>(child, "Rank")).ToList()).List();
        return builder;
    }

    internal static ShadowReadSeed[] Seeds() =>
    [
        new(1, "one", false, 1, 20, "a", null, false, ShadowReadStatus.Draft, [1, 2]),
        new(2, "two", false, 1, 10, "a", 7, false, ShadowReadStatus.Published, null),
        new(3, "deleted", false, 1, 5, "b", 8, true, ShadowReadStatus.Draft, [3]),
        new(4, "hidden", true, 1, 40, "b", null, false, ShadowReadStatus.Published, [4]),
        new(5, "archived folder", false, 2, 30, "c", 9, false, ShadowReadStatus.Published, [5])
    ];
    internal static ShadowReadFolderSeed[] Folders() => [new(1, "open", false), new(2, "archived", true)];

    internal static (object Notes, object Folders, InMemoryDataContext Data) Fixture(Assembly? assembly = null)
    {
        assembly ??= Generated.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ShadowReadContextRepositoryFixture")!)!;
        var data = (InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        NoteStore(data).SeedRange(Seeds());
        FolderStore(data).SeedRange(Folders());
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ShadowReadNoteRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ShadowReadFolderRepositoryTestDouble")!, fixture)!, data);
    }

    internal static InMemoryStore<ShadowReadSeed, int> NoteStore(InMemoryDataContext data) => ModelFilterTestHarness.Store<ShadowReadSeed>(data, nameof(ShadowReadNote));
    internal static InMemoryStore<ShadowReadFolderSeed, int> FolderStore(InMemoryDataContext data) => ModelFilterTestHarness.Store<ShadowReadFolderSeed>(data, nameof(ShadowReadFolder));
}
