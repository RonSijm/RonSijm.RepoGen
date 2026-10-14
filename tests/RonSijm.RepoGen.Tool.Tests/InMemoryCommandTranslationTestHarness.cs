using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class InMemoryCommandTranslationTestHarness
{
    internal const string Namespace = "Generated.CommandTranslations";
    private static readonly Lazy<Assembly> Entities = new(() => Compile(Builder()));
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(Builder(mapped: true)));

    internal static RepositoryModelBuilder Builder(bool mapped = false, bool optIn = true, bool translations = true,
        bool backend = true, bool queries = true, bool strict = true, bool keyed = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries(queries)
            .UseInMemoryTestCommands(backend).RequirePortableTestCommands(strict && backend));
        if (keyed) builder.Configure(options => options.UseKeyedContextLeases<string>());
        if (translations) Register(builder);
        var entity = builder.Entity<MemoryWriteNote>();
        if (mapped) entity.HasInMemoryStore<TranslatedWriteSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Version, row.Hidden))
            .MapInMemoryEntity<TranslatedWriteSeed>(row => new MemoryWriteNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden })
            .MapInMemoryWrites<TranslatedWriteSeed>((row, saved) => new(row.Id, row.Title, row.Version, row.Hidden));
        else entity.HasInMemoryStore(row => row.Id, row => new MemoryWriteNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden });
        entity.HasVisibilityPolicy("Visible", row => !row.Hidden && TranslationCalls.Measure(row.Title) > 0);
        if (!keyed) entity.HasQuery("List").OrderBy(row => row.Id).Select(row => new MemoryWriteView(row.Id, row.Title, row.Version)).List();
        void Configure(RepositoryCommandBuilder<MemoryWriteNote> command)
        {
            command.UseInMemoryTranslations(optIn);
            if (keyed) command.UseContextKey<MemoryWriteRequest, string>(request => TranslatedWriteCalls.Normalize(request.ContextKey));
        }
        Configure(entity.HasCommand("Create").Create<MemoryWriteRequest>(request => new MemoryWriteNote
            { Id = TranslationCalls.Echo(request.Id), Title = TranslatedWriteCalls.Normalize(request.Title), Version = TranslationCalls.Echo(1), Hidden = TranslationCalls.Echo(false) })
            .AllowWrite(row => row.Id).AllowWrite(row => row.Version)
            .Validate<MemoryWriteRequest>(request => TranslationCalls.Positive(request.Id) && TranslationCalls.Measure(request.Title) > 0, "A positive id and title are required.")
            .Save().Returns(row => new MemoryWriteView(TranslationCalls.Echo(row.Id), row.Title, TranslationCalls.Shift(row.Version, 0))));
        foreach (var name in new[] { "Rename", "StageRename", "SavedRename" })
        {
            var command = entity.HasCommand(name).Where<MemoryWriteRequest>((row, request) => row.Id == TranslationCalls.Echo(request.Id) && Microsoft.EntityFrameworkCore.EF.Functions.Like(row.Title, "%"))
                .Patch(setters => setters.SetPropertyWhen<string, MemoryWriteRequest>(row => row.Title, (row, request) => TranslatedWriteCalls.Normalize(request.Title), request => TranslationCalls.Echo(request.Supplied))
                    .SetProperty(row => row.Version, row => TranslationCalls.Shift(row.Version, 1))).AllowWrite(row => row.Version)
                .ExpectVersion<int, MemoryWriteRequest>(row => row.Version, (row, request) => TranslationCalls.Echo(request.ExpectedVersion));
            if (name == "StageRename") command.Stage();
            else if (name == "SavedRename") command.Save().ReturnsSaved(row => new MemoryWriteView(row.Id, TranslatedWriteCalls.Normalize(row.Title), TranslationCalls.Measure(row.Title)));
            else command.Save().Returns(row => new MemoryWriteView(TranslationCalls.Echo(row.Id), row.Title, TranslationCalls.Echo(row.Version)));
            Configure(command);
        }
        Configure(entity.HasCommand("Delete").Where<MemoryWriteRequest>((row, request) => row.Id == TranslationCalls.Echo(request.Id))
            .TrackedDelete().ExpectVersion<int, MemoryWriteRequest>(row => row.Version, (row, request) => TranslationCalls.Echo(request.ExpectedVersion))
            .Save().Returns(row => new MemoryWriteView(row.Id, row.Title, TranslationCalls.Echo(row.Version))));
        Configure(entity.HasCommand("Unknown").Where<MemoryWriteRequest>((row, request) => row.Id == request.Id)
            .Patch(setters => setters.SetProperty(row => row.Version, row => TranslationCalls.Unit(row.Version, "different")))
            .AllowWrite(row => row.Version).Stage().RequireTestOverride("No translation for this fixed literal."));
        if (keyed) return builder;
        foreach (var name in new[] { "RenameMany", "StageMany" })
        {
            var command = entity.HasCommand(name).PatchMany<MemoryWriteRequest, int, IReadOnlyList<MemoryWriteRequest>>(
                items => items.Where(item => TranslationCalls.Positive(item.Id)).ToArray(), row => row.Id, item => TranslationCalls.Echo(item.Id),
                setters => setters.SetPropertyWhen(row => row.Title, (row, item) => TranslatedWriteCalls.Normalize(item.Title), item => TranslationCalls.Echo(item.Supplied))
                    .SetProperty(row => row.Version, row => TranslationCalls.Shift(row.Version, 1))
                    .ExpectVersion(row => row.Version, (row, item) => TranslationCalls.Echo(item.ExpectedVersion)), maximumItems: 3).AllowWrite(row => row.Version);
            if (name == "StageMany") command.Stage();
            else command.Save().ReturnsSaved(row => new MemoryWriteView(row.Id, row.Title, TranslationCalls.Measure(row.Title)));
            Configure(command);
        }
        Configure(entity.HasCommand("DeleteMany").DeleteMany<MemoryWriteRequest, int, IReadOnlyList<MemoryWriteRequest>>(
            items => items.Where(item => TranslationCalls.Positive(item.Id)).ToArray(), row => row.Id, item => TranslationCalls.Echo(item.Id),
            setters => setters.ExpectVersion(row => row.Version, (row, item) => TranslationCalls.Echo(item.ExpectedVersion)), maximumItems: 3)
            .Save().Returns(row => new MemoryWriteView(row.Id, row.Title, TranslationCalls.Echo(row.Version))));
        Configure(entity.HasCommand("CreateMany").CreateMany<MemoryWriteRequest, IReadOnlyList<MemoryWriteRequest>>(
            items => items.Where(item => TranslationCalls.Positive(item.Id)).ToArray(), item => new MemoryWriteNote
                { Id = TranslationCalls.Echo(item.Id), Title = TranslatedWriteCalls.Normalize(item.Title), Version = TranslationCalls.Echo(1), Hidden = TranslationCalls.Echo(false) }, maximumItems: 3)
            .AllowWrite(row => row.Id).AllowWrite(row => row.Version).Save()
            .ReturnsSaved(row => new MemoryWriteView(row.Id, row.Title, TranslationCalls.Measure(row.Title))));
        return builder;
    }

    internal static void Register(RepositoryModelBuilder builder)
    {
        InMemoryTranslationTestHarness.Register(builder);
        builder.HasInMemoryTranslation<string, string>("NormalizedText", value => TranslatedWriteCalls.Normalize(value), value => value.Trim().ToUpperInvariant());
    }
    internal static Assembly Compile(RepositoryModelBuilder builder)
    {
        using var context = MemoryWriteTestHarness.Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }
    internal static (object Repository, object Fixture, InMemoryWriteDataContext Data) Fixture(bool mapped = false, Assembly? assembly = null, string? key = null)
    {
        assembly ??= mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryWriteContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)(key is null ? fixture.GetType().GetProperty("Data")!.GetValue(fixture)! : fixture.GetType().GetMethod("For")!.Invoke(fixture, [key])!);
        if (mapped) Store<TranslatedWriteSeed>(data).SeedRange(Seeds().Select(row => new TranslatedWriteSeed(row.Id, row.Title, row.Version, row.Hidden)));
        else Store<MemoryWriteNote>(data).SeedRange(Seeds());
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryWriteNoteRepositoryTestDouble")!, fixture)!, fixture, data);
    }
    internal static InMemoryStore<T, int> Store<T>(InMemoryWriteDataContext data) where T : class
        => ManyReadTestHarness.Store<T, int>(data, nameof(MemoryWriteNote));
    internal static MemoryWriteNote[] Seeds() => [new() { Id = 1, Title = "old", Version = 1 }, new() { Id = 2, Title = "hidden", Version = 1, Hidden = true }, new() { Id = 3, Title = "other", Version = 1 }];
    internal static TranslatedWriteSeed[] Rows(InMemoryWriteDataContext data, bool mapped)
        => (mapped ? Store<TranslatedWriteSeed>(data).Snapshot() : Store<MemoryWriteNote>(data).Snapshot().Select(row => new TranslatedWriteSeed(row.Id, row.Title, row.Version, row.Hidden))).OrderBy(row => row.Id).ToArray();
}
