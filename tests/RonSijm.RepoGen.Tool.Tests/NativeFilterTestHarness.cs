using System.Reflection;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class NativeFilterTestHarness
{
    internal const string Namespace = "Generated.NativeFilters";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    private static Assembly Compile(bool mapped)
    {
        using var context = ModelWriteFilterTestHarness.Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }

    internal static RepositoryModelBuilder Base(bool mapped = true, bool strict = true, bool backend = true, bool queries = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseReturningWrites<SqliteReturningWriter>().UseScriptedTestDoubles(Namespace + ".Testing")
            .UseInMemoryTestStores().UseInMemoryTestQueries(queries).UseInMemoryTestCommands(backend).RequirePortableTestCommands(strict && backend));
        var entity = builder.Entity<ModelWriteFilterNote>();
        if (mapped) entity.HasInMemoryStore<ModelWriteFilterSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Version, row.Deleted, row.Hidden))
            .MapInMemoryEntity<ModelWriteFilterSeed>(row => new ModelWriteFilterNote { Id = row.Id, Title = row.Title, Version = row.Version, Deleted = row.Deleted, Hidden = row.Hidden })
            .MapInMemoryWrites<ModelWriteFilterSeed>((row, saved) => new(row.Id, row.Title, row.Version, row.Deleted, row.Hidden));
        else entity.HasInMemoryStore(row => row.Id, row => new ModelWriteFilterNote { Id = row.Id, Title = row.Title, Version = row.Version, Deleted = row.Deleted, Hidden = row.Hidden });
        entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
        if (queries)
        {
            entity.HasQuery("All").IgnoreQueryFilters().BypassVisibility("Visible").OrderBy(row => row.Id)
                .Select(row => new ModelWriteFilterSeed(row.Id, row.Title, row.Version, row.Deleted, row.Hidden)).List();
            entity.UseInMemoryModelQueryFilter();
            entity.HasQuery("Cards").OrderBy(row => row.Id).Select(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted)).List();
        }
        return builder;
    }

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool filter = true, bool simulate = true, bool strict = true, bool backend = true, bool queries = true)
    {
        var builder = Base(mapped, strict, backend, queries); var entity = builder.Entity<ModelWriteFilterNote>();
        void Configure(RepositoryCommandBuilder<ModelWriteFilterNote> command) => command.SimulateImmediateWrites(simulate).UseInMemoryModelQueryFilter(filter);
        foreach (var name in new[] { "Update", "UpdateBypass" })
        {
            var command = entity.HasCommand(name).Where<ModelWriteFilterPatch>((row, input) => row.Id == input.Id)
                .ExpectVersion<int, ModelWriteFilterPatch>(row => row.Version, (row, input) => input.ExpectedVersion)
                .Update(setters => setters.SetProperty<string, ModelWriteFilterPatch>(row => row.Title, (row, input) => input.Title)
                    .SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                .ReturnsMany(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted));
            if (name == "UpdateBypass") command.BypassVisibility("Visible");
            Configure(command);
        }
        Configure(entity.HasCommand("Archive").Where<int>((row, id) => row.Id == id)
            .Update(setters => setters.SetProperty(row => row.Deleted, true).SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
            .ReturnsMany(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted)));
        Configure(entity.HasCommand("ArchiveAll").Where(row => row.Id > 0)
            .Update(setters => setters.SetProperty(row => row.Deleted, true).SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
            .ReturnsMany(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted)));
        Configure(entity.HasCommand("Mark").Where<int>((row, id) => row.Id == id).Update(setters => setters.SetProperty(row => row.Deleted, true))
            .ReturnsMany(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted)));
        foreach (var name in new[] { "Consume", "ConsumeBypass" })
        {
            var command = entity.HasCommand(name).Where<ModelWriteFilterPatch>((row, input) => row.Id == input.Id)
                .ExpectVersion<int, ModelWriteFilterPatch>(row => row.Version, (row, input) => input.ExpectedVersion).Delete()
                .ReturnsMany(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted));
            if (name == "ConsumeBypass") command.BypassVisibility("Visible");
            Configure(command);
        }
        Configure(entity.HasCommand("ConsumeAll").Where(row => row.Id > 0).Delete().ReturnsMany(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted)));
        Configure(entity.HasCommand("FailUpdateResult").Where(row => row.Id > 0).Update(setters => setters.SetProperty(row => row.Title, "not committed"))
            .ReturnsMany(row => new NativeFilterThrowingCard(row.Id)));
        Configure(entity.HasCommand("FailDeleteResult").Where(row => row.Id > 0).Delete().ReturnsMany(row => new NativeFilterThrowingCard(row.Id)));
        entity.HasCommand("StageTitle").Where<ModelWriteFilterPatch>((row, input) => row.Id == input.Id)
            .Patch(setters => setters.SetProperty<string, ModelWriteFilterPatch>(row => row.Title, (row, input) => input.Title))
            .ExpectVersion<int, ModelWriteFilterPatch>(row => row.Version, (row, input) => input.ExpectedVersion).UseInMemoryModelQueryFilter(filter).Stage();
        return builder;
    }

    internal static (object Repository, InMemoryWriteDataContext Data) Fixture(bool mapped = true, Assembly? assembly = null)
    {
        assembly ??= mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelWriteFilterContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped) ModelWriteFilterTestHarness.Store<ModelWriteFilterSeed>(data).SeedRange(ModelWriteFilterTestHarness.Seeds().Select(row => new ModelWriteFilterSeed(row.Id, row.Title, row.Version, row.Deleted, row.Hidden)));
        else ModelWriteFilterTestHarness.Store<ModelWriteFilterNote>(data).SeedRange(ModelWriteFilterTestHarness.Seeds());
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelWriteFilterNoteRepositoryTestDouble")!, fixture)!, data);
    }
    internal static Task<T> Call<T>(object repository, string name, params object?[] inputs)
        => MemoryWriteTestHarness.Invoke<T>(repository, name + "Async", inputs);
}
