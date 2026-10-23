using System.Reflection;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class ProjectionTranslationTestHarness
{
    internal const string Namespace = "Generated.ProjectionTranslations";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    private static Assembly Compile(bool mapped)
    {
        using var context = MemoryWriteTestHarness.Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool translations = true, bool queries = true, bool commands = true,
        bool commandOptIn = true, bool strict = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries(queries).UseInMemoryTestCommands(commands)
            .RequirePortableTestQueries(strict && queries).RequirePortableTestCommands(strict && commands));
        if (translations) Register(builder);
        var entity = builder.Entity<MemoryWriteNote>();
        if (mapped) entity.HasInMemoryStore<TranslatedWriteSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Version, row.Hidden))
            .MapInMemoryEntity<TranslatedWriteSeed>(row => new MemoryWriteNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden })
            .MapInMemoryWrites<TranslatedWriteSeed>((row, saved) => new(row.Id, row.Title, row.Version, row.Hidden));
        else entity.HasInMemoryStore(row => row.Id, row => new MemoryWriteNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden });
        entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
        entity.HasProjection<ProjectionNoteCard>(row => ProjectionNoteCalls.Card(row.Id, row.Title, row.Version, row.Hidden));
        entity.HasQuery("Cards").OrderBy(row => row.Id).ProjectTo<ProjectionNoteCard>().List();
        entity.HasQuery("Get").Where<int>((row, id) => row.Id == id).ProjectTo<ProjectionNoteCard>().SingleOrDefault();
        entity.HasQuery("Page").OrderBy(row => row.Id).ProjectTo<ProjectionNoteCard>().Paged();
        entity.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<ProjectionNoteCard>().Stream();
        entity.HasQuery("Source").From(sources => sources.Set<MemoryWriteNote>().OrderBy(row => row.Id)
            .Select(row => ProjectionNoteCalls.Card(row.Id, row.Title, row.Version, row.Hidden))).List();
        entity.HasQuery("Mutable").OrderBy(row => row.Id).Select(row => ProjectionNoteCalls.Data(row.Id, row.Title)).List();
        entity.HasQuery("Stamps").OrderBy(row => row.Id).Select(row => ProjectionNoteCalls.Stamp(row.Id)).List();
        entity.HasQuery("Nullable").OrderBy(row => row.Id).Select(row => ProjectionNoteCalls.Optional(-row.Id, row.Title))
            .ResultNullability<ProjectionNoteCard>(type => type.Nullable()).List();
        entity.HasQuery("Groups").GroupBy(row => row.Hidden, group => ProjectionNoteCalls.Group(group.Key, group.Count(), group.Max(row => row.Version))).List();
        entity.HasQuery("Literal").OrderBy(row => row.Id).Select(row => ProjectionNoteCalls.Literal(row.Id, row.Title, "note:")).List();
        entity.HasQuery("WrongLiteral").RequireTestOverride("A different fixed literal has no declared projection translation.")
            .Select(row => ProjectionNoteCalls.Literal(row.Id, row.Title, "other:")).List();
        entity.HasQuery("Count").Count();
        entity.HasQuery("Never").OrderBy(row => row.Id).Select(row => ProjectionNoteCalls.Never(row.Id, row.Title)).List();
        foreach (var name in new[] { "Rename", "SavedRename", "StageRename" })
        {
            var command = entity.HasCommand(name).Where<ProjectionNotePatch>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, ProjectionNotePatch>(row => row.Title, (row, request) => request.Title))
                .ExpectVersion<int, ProjectionNotePatch>(row => row.Version, (row, request) => request.ExpectedVersion)
                .UseInMemoryTranslations(commandOptIn);
            if (name == "StageRename") command.Stage();
            else if (name == "SavedRename") command.Save().ReturnsSaved(row => ProjectionNoteCalls.Card(row.Id, row.Title, row.Version, row.Hidden));
            else command.Save().Returns(row => ProjectionNoteCalls.Card(row.Id, row.Title, row.Version, row.Hidden));
        }
        entity.HasCommand("RenameMany").PatchMany<ProjectionNotePatch, int, IReadOnlyList<ProjectionNotePatch>>(items => items, row => row.Id, item => item.Id,
            setters => setters.SetProperty(row => row.Title, (row, item) => item.Title).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion), maximumItems: 4)
            .UseInMemoryTranslations(commandOptIn).Save().ReturnsSaved(row => ProjectionNoteCalls.Card(row.Id, row.Title, row.Version, row.Hidden));
        return builder;
    }

    internal static void Register(RepositoryModelBuilder builder)
    {
        builder.HasInMemoryTranslation<string, int>("Length", value => TranslationCalls.Measure(value), value => value.Length);
        builder.HasInMemoryProjectionTranslation<int, string, int, bool, ProjectionNoteCard>("Card",
            (id, title, version, hidden) => ProjectionNoteCalls.Card(id, title, version, hidden),
            (id, title, version, hidden) => new ProjectionNoteCard(id, (hidden ? "hidden:" : "") + title.Trim().ToUpperInvariant(), version, TranslationCalls.Measure(title)));
        builder.HasInMemoryProjectionTranslation<int, string, int, bool, ProjectionNoteCard>("Alias",
            (id, title, version, hidden) => ProjectionNoteCalls.Alias(id, title, version, hidden),
            (id, title, version, hidden) => ProjectionNoteCalls.Card(id, title, version, hidden));
        builder.HasInMemoryProjectionTranslation<int, string, ProjectionNoteData>("Data", (id, title) => ProjectionNoteCalls.Data(id, title),
            (id, title) => new ProjectionNoteData { Id = id, Caption = title });
        builder.HasInMemoryProjectionTranslation<int, ProjectionNoteStamp>("Stamp", id => ProjectionNoteCalls.Stamp(id), id => new ProjectionNoteStamp(id));
        builder.HasInMemoryProjectionTranslation<bool, int, int, ProjectionNoteGroup>("Group", (hidden, count, maximum) => ProjectionNoteCalls.Group(hidden, count, maximum),
            (hidden, count, maximum) => new ProjectionNoteGroup(hidden, count, maximum));
        builder.HasInMemoryProjectionTranslation<int, string, ProjectionNoteCard?>("Optional", (id, title) => ProjectionNoteCalls.Optional(id, title),
            (id, title) => id < 0 ? null : new ProjectionNoteCard(id, title, 1, title.Length));
        builder.HasInMemoryProjectionTranslation<int, string, ProjectionNoteCard>("Literal", (id, title) => ProjectionNoteCalls.Literal(id, title, "note:"),
            (id, title) => new ProjectionNoteCard(id, "note:" + title, 1, title.Length));
        builder.HasInMemoryProjectionTranslation<int, string, ProjectionNoteCard>("Never", (id, title) => ProjectionNoteCalls.Never(id, title),
            (id, title) => new ProjectionNoteCard(id, title, 1, title.Length));
    }

    internal static MemoryWriteNote[] Seeds() => [new() { Id = 1, Title = " alpha ", Version = 1 }, new() { Id = 2, Title = "beta", Version = 2 },
        new() { Id = 3, Title = "hidden", Version = 1, Hidden = true }];
    internal static (object Repository, InMemoryWriteDataContext Data) Fixture(bool mapped = true, Assembly? assembly = null)
    {
        assembly ??= mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryWriteContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped) Store<TranslatedWriteSeed>(data).SeedRange(Seeds().Select(row => new TranslatedWriteSeed(row.Id, row.Title, row.Version, row.Hidden)));
        else Store<MemoryWriteNote>(data).SeedRange(Seeds());
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryWriteNoteRepositoryTestDouble")!, fixture)!, data);
    }
    internal static InMemoryStore<T, int> Store<T>(InMemoryDataContext data) where T : class =>
        (InMemoryStore<T, int>)data.GetType().GetProperty("MemoryWriteNoteStore")!.GetValue(data)!;
}
