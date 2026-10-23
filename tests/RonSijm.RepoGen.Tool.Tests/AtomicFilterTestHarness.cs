using System.Linq.Expressions;
using System.Reflection;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class AtomicFilterTestHarness
{
    internal const string Namespace = "Generated.AtomicFilters";
    private static readonly string[] QueryNames = ["Cards", "All"];
    private static readonly string[] EnsureNames = ["Ensure", "EnsureCount"];
    private static readonly string[] PutNames = ["Put", "PutCount", "PutBypass", "ArchivePut", "ArchiveCount", "HidePut"];
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    private static Assembly Compile(bool mapped)
    {
        using var context = ModelWriteFilterTestHarness.Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }

    internal static RepositoryModelBuilder Base(bool mapped = true, bool strict = true, bool queries = true, bool backend = true, bool keyed = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseAtomicWrites<SqliteAtomicWriter>().UseScriptedTestDoubles(Namespace + ".Testing")
            .UseInMemoryTestStores().UseInMemoryTestQueries(queries).UseInMemoryTestCommands(backend).RequirePortableTestCommands(strict && backend));
        if (keyed) builder.Configure(options => options.UseKeyedContextLeases<string>());
        var row = builder.Entity<ModelWriteFilterNote>(); row.UseInMemoryModelQueryFilter();
        if (mapped) row.HasInMemoryStore<ModelWriteFilterSeed, int>(item => item.Id, item => new(item.Id, item.Title, item.Version, item.Deleted, item.Hidden))
            .MapInMemoryEntity<ModelWriteFilterSeed>(item => new ModelWriteFilterNote { Id = item.Id, Title = item.Title, Version = item.Version, Deleted = item.Deleted, Hidden = item.Hidden })
            .MapInMemoryWrites<ModelWriteFilterSeed>((item, saved) => new(item.Id, item.Title, item.Version, item.Deleted, item.Hidden));
        else row.HasInMemoryStore(item => item.Id, item => new ModelWriteFilterNote { Id = item.Id, Title = item.Title, Version = item.Version, Deleted = item.Deleted, Hidden = item.Hidden });
        row.HasVisibilityPolicy("Visible", item => !item.Hidden);
        if (queries)
        {
            foreach (var name in QueryNames)
            {
                var query = row.HasQuery(name).OrderBy(item => item.Id);
                if (name == "All") query.IgnoreQueryFilters().BypassVisibility("Visible");
                if (keyed) query.UseContextKey<AtomicFilterInput, string>(request => request.Partition);
                query.Select(item => new ModelWriteFilterSeed(item.Id, item.Title, item.Version, item.Deleted, item.Hidden)).List();
            }
        }
        return builder;
    }

    internal static Expression<Func<AtomicFilterInput, ModelWriteFilterNote>> Create => request => new ModelWriteFilterNote
        { Id = request.Id, Title = request.Title, Version = 0, Deleted = request.Deleted, Hidden = request.Hidden };

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool filter = true, bool simulate = true, bool strict = true,
        bool queries = true, bool backend = true, bool keyed = false)
    {
        var builder = Base(mapped, strict, queries, backend, keyed); var entity = builder.Entity<ModelWriteFilterNote>();
        void Configure(RepositoryCommandBuilder<ModelWriteFilterNote> command)
        {
            command.AllowWrite(row => row.Id).AllowWrite(row => row.Version).SimulateImmediateWrites(simulate).UseInMemoryModelQueryFilter(filter);
            if (keyed) command.UseContextKey<AtomicFilterInput, string>(request => request.Partition);
        }
        foreach (var name in EnsureNames)
        {
            var command = entity.HasCommand(name).Validate<AtomicFilterInput>(request => request.Id > 0, "A positive id is required.")
                .Create(Create).InsertIfAbsent(row => row.Id);
            if (name == "Ensure") command.Returns(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted));
            Configure(command);
        }
        foreach (var name in PutNames)
        {
            var command = entity.HasCommand(name).Create(Create).InsertOrUpdate(row => row.Id, setters =>
            {
                setters.SetProperty<string, AtomicFilterInput>(row => row.Title, (row, request) => request.Title)
                    .SetProperty(row => row.Version, row => row.Version + 1);
                if (name is "ArchivePut" or "ArchiveCount") setters.SetProperty(row => row.Deleted, true);
                if (name == "HidePut") setters.SetProperty(row => row.Hidden, true);
            }).ExpectVersion<int, AtomicFilterInput>(row => row.Version, (row, request) => request.ExpectedVersion);
            if (name == "PutBypass") command.BypassVisibility("Visible");
            if (name is not "PutCount" and not "ArchiveCount") command.Returns(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted));
            Configure(command);
        }
        var stage = entity.HasCommand("StageRename").Where<AtomicFilterInput>((row, request) => row.Id == request.Id)
            .Patch(setters => setters.SetProperty<string, AtomicFilterInput>(row => row.Title, (row, request) => request.Title))
            .ExpectVersion<int, AtomicFilterInput>(row => row.Version, (row, request) => request.ExpectedVersion).UseInMemoryModelQueryFilter(filter).Stage();
        var create = entity.HasCommand("StageCreate").Create(Create).AllowWrite(row => row.Id).AllowWrite(row => row.Version).UseInMemoryModelQueryFilter(filter).Stage();
        if (keyed) { stage.UseContextKey<AtomicFilterInput, string>(request => request.Partition); create.UseContextKey<AtomicFilterInput, string>(request => request.Partition); }
        return builder;
    }

    internal static (object Repository, InMemoryWriteDataContext Data, object Fixture) Fixture(bool mapped = true, Assembly? assembly = null, string? key = null)
    {
        assembly ??= mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelWriteFilterContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)(key is null ? fixture.GetType().GetProperty("Data")!.GetValue(fixture)! : fixture.GetType().GetMethod("For")!.Invoke(fixture, [key])!);
        Seed(data, mapped);
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelWriteFilterNoteRepositoryTestDouble")!, fixture)!, data, fixture);
    }
    internal static void Seed(InMemoryWriteDataContext data, bool mapped)
    {
        if (mapped) ModelWriteFilterTestHarness.Store<ModelWriteFilterSeed>(data).ReplaceWith(ModelWriteFilterTestHarness.Seeds().Select(item => new ModelWriteFilterSeed(item.Id, item.Title, item.Version, item.Deleted, item.Hidden)));
        else ModelWriteFilterTestHarness.Store<ModelWriteFilterNote>(data).ReplaceWith(ModelWriteFilterTestHarness.Seeds());
    }
    internal static Task<T> Call<T>(object repository, string name, params object?[] input) => MemoryWriteTestHarness.Invoke<T>(repository, name + "Async", input);
}
