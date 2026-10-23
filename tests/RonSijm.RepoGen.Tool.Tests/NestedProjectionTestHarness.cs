using System.Reflection;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class NestedProjectionTestHarness
{
    internal const string Namespace = "Generated.NestedProjections";
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));

    private static Assembly Compile(bool mapped)
    {
        using var context = MemoryWriteTestHarness.Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool translations = true, bool queries = true,
        bool commands = true, bool commandConsent = true, bool strict = true)
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
        entity.HasProjection<NestedProjectionCard>(row => NestedProjectionCalls.Alias(row.Id, row.Title, row.Version, row.Hidden));
        entity.HasQuery("Cards").OrderBy(row => row.Id).ProjectTo<NestedProjectionCard>().List();
        entity.HasQuery("Find").Where<int>((row, id) => row.Id == id).ProjectTo<NestedProjectionCard>().SingleOrDefault();
        entity.HasQuery("All").BypassVisibility("Visible").OrderBy(row => row.Id).ProjectTo<NestedProjectionCard>().List();
        entity.HasQuery("Page").OrderBy(row => row.Id).ProjectTo<NestedProjectionCard>().Paged();
        entity.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<NestedProjectionCard>().Stream();
        entity.HasQuery("Source").From(sources => sources.Set<MemoryWriteNote>().OrderBy(row => row.Id)
            .Select(row => NestedProjectionCalls.Array(row.Id, row.Title))).List();
        entity.HasQuery("Arrays").OrderBy(row => row.Id).Select(row => NestedProjectionCalls.Array(row.Id, row.Title)).List();
        entity.HasQuery("Materialized").OrderBy(row => row.Id).Select(row => NestedProjectionCalls.Materialized(row.Id, row.Title)).List();
        entity.HasQuery("Editable").OrderBy(row => row.Id).Select(row => NestedProjectionCalls.Editable(row.Id, row.Title)).List();
        entity.HasQuery("Stamps").OrderBy(row => row.Id).Select(row => NestedProjectionCalls.Stamp(row.Id)).List();
        entity.HasQuery("Optional").OrderBy(row => row.Id).Select(row => NestedProjectionCalls.Optional(-row.Id, row.Title))
            .ResultNullability<NestedProjectionCard>(type => type.Nullable()).List();
        entity.HasQuery("Groups").BypassVisibility("Visible").GroupBy(row => row.Hidden,
            group => NestedProjectionCalls.Group(group.Key, group.Count(), group.Max(row => row.Version))).List();
        entity.HasQuery("Never").OrderBy(row => row.Id).Select(row => NestedProjectionCalls.Never(row.Id, row.Title)).List();
        entity.HasQuery("Count").Count();
        foreach (var name in new[] { "Rename", "SavedRename", "StageRename" })
        {
            var command = entity.HasCommand(name).Where<ProjectionNotePatch>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, ProjectionNotePatch>(row => row.Title, (row, request) => request.Title))
                .ExpectVersion<int, ProjectionNotePatch>(row => row.Version, (row, request) => request.ExpectedVersion)
                .UseInMemoryTranslations(commandConsent);
            if (name == "StageRename") command.Stage();
            else if (name == "SavedRename") command.Save().ReturnsSaved(row => NestedProjectionCalls.Card(row.Id, row.Title, row.Version, row.Hidden));
            else command.Save().Returns(row => NestedProjectionCalls.Card(row.Id, row.Title, row.Version, row.Hidden));
        }
        entity.HasCommand("RenameMany").PatchMany<ProjectionNotePatch, int, IReadOnlyList<ProjectionNotePatch>>(items => items,
            row => row.Id, item => item.Id,
            setters => setters.SetProperty(row => row.Title, (row, item) => item.Title).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion), maximumItems: 4)
            .UseInMemoryTranslations(commandConsent).Save().ReturnsSaved(row => NestedProjectionCalls.Card(row.Id, row.Title, row.Version, row.Hidden));
        return builder;
    }

    internal static void Register(RepositoryModelBuilder builder)
    {
        builder.HasInMemoryProjectionTranslation<string, int, NestedProjectionDetail>("Detail", (caption, value) => NestedProjectionCalls.Detail(caption, value),
            (caption, value) => new NestedProjectionDetail(caption.Trim().ToUpperInvariant(), value));
        builder.HasInMemoryProjectionTranslation<int, string, int, bool, NestedProjectionCard>("Card",
            (id, title, version, hidden) => NestedProjectionCalls.Card(id, title, version, hidden),
            (id, title, version, hidden) => new NestedProjectionCard(id, hidden ? null : NestedProjectionCalls.Detail(title, version),
                new List<NestedProjectionDetail> { new("length", title.Length), new("version", version) },
                new[] { title.Trim(), hidden ? "hidden" : "visible" }), allowNestedData: true);
        builder.HasInMemoryProjectionTranslation<int, string, int, bool, NestedProjectionCard>("Alias",
            (id, title, version, hidden) => NestedProjectionCalls.Alias(id, title, version, hidden),
            (id, title, version, hidden) => NestedProjectionCalls.Card(id, title, version, hidden), allowNestedData: true);
        builder.HasInMemoryProjectionTranslation<int, string, NestedProjectionCard>("Array", (id, title) => NestedProjectionCalls.Array(id, title),
            (id, title) => new NestedProjectionCard(id, new NestedProjectionDetail(title, id),
                new[] { new NestedProjectionDetail(title, title.Length) }, Array.Empty<string>()), allowNestedData: true);
        builder.HasInMemoryProjectionTranslation<int, string, NestedProjectionCard>("Materialized", (id, title) => NestedProjectionCalls.Materialized(id, title),
            (id, title) => new NestedProjectionCard(id, null,
                Enumerable.Range(0, 2).Select(offset => new NestedProjectionDetail(title, id + offset)).OrderBy(row => row.Value).ThenBy(row => row.Caption).ToList(),
                title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => word.ToUpperInvariant()).ToArray()), allowNestedData: true);
        builder.HasInMemoryProjectionTranslation<int, string, NestedProjectionEditable>("Editable", (id, title) => NestedProjectionCalls.Editable(id, title),
            (id, title) => new NestedProjectionEditable { Id = id, Detail = new NestedProjectionDetail(title, id),
                Items = new List<NestedProjectionDetail> { new(title, title.Length) }, Labels = new[] { title } }, allowNestedData: true);
        builder.HasInMemoryProjectionTranslation<int, NestedProjectionStamp>("Stamp", id => NestedProjectionCalls.Stamp(id),
            id => new NestedProjectionStamp(id, new NestedProjectionDetail("stamp", id)), allowNestedData: true);
        builder.HasInMemoryProjectionTranslation<int, string, NestedProjectionCard?>("Optional", (id, title) => NestedProjectionCalls.Optional(id, title),
            (id, title) => id < 0 ? null : NestedProjectionCalls.Array(id, title), allowNestedData: true);
        builder.HasInMemoryProjectionTranslation<bool, int, int, NestedProjectionCard>("Group", (hidden, count, maximum) => NestedProjectionCalls.Group(hidden, count, maximum),
            (hidden, count, maximum) => NestedProjectionCalls.Card(count, "group", maximum, hidden), allowNestedData: true);
        builder.HasInMemoryProjectionTranslation<int, string, NestedProjectionCard>("Never", (id, title) => NestedProjectionCalls.Never(id, title),
            (id, title) => new NestedProjectionCard(id, new NestedProjectionDetail(title, id), new List<NestedProjectionDetail>(), Array.Empty<string>()), allowNestedData: true);
    }

    internal static (object Repository, InMemoryWriteDataContext Data) Fixture(bool mapped = true, Assembly? assembly = null)
    {
        assembly ??= mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryWriteContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped) ProjectionTranslationTestHarness.Store<TranslatedWriteSeed>(data).SeedRange(ProjectionTranslationTestHarness.Seeds()
            .Select(row => new TranslatedWriteSeed(row.Id, row.Title, row.Version, row.Hidden)));
        else ProjectionTranslationTestHarness.Store<MemoryWriteNote>(data).SeedRange(ProjectionTranslationTestHarness.Seeds());
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryWriteNoteRepositoryTestDouble")!, fixture)!, data);
    }
}
