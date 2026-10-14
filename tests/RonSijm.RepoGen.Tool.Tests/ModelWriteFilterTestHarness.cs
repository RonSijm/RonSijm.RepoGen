using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class ModelWriteFilterTestHarness
{
    internal const string Namespace = "Generated.ModelWriteFilters";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    private static Assembly Compile(bool mapped)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }
    internal static ModelWriteFilterContext Context() => new(new DbContextOptionsBuilder<ModelWriteFilterContext>().UseSqlite("Data Source=:memory:").Options);

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool enabled = true, bool strict = true, bool backend = true,
        bool queries = true, bool readFilters = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries(queries)
            .UseInMemoryTestCommands(backend).RequirePortableTestQueries(strict && queries && readFilters).RequirePortableTestCommands(strict && backend));
        var entity = builder.Entity<ModelWriteFilterNote>(); entity.UseInMemoryModelQueryFilter(readFilters);
        if (mapped) entity.HasInMemoryStore<ModelWriteFilterSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Version, row.Deleted, row.Hidden))
            .MapInMemoryEntity<ModelWriteFilterSeed>(row => new ModelWriteFilterNote { Id = row.Id, Title = row.Title, Version = row.Version, Deleted = row.Deleted, Hidden = row.Hidden })
            .MapInMemoryWrites<ModelWriteFilterSeed>((row, saved) => new(row.Id, row.Title, row.Version, row.Deleted, row.Hidden));
        else entity.HasInMemoryStore(row => row.Id, row => new ModelWriteFilterNote { Id = row.Id, Title = row.Title, Version = row.Version, Deleted = row.Deleted, Hidden = row.Hidden });
        entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
        if (queries)
        {
            entity.HasQuery("Cards").OrderBy(row => row.Id).Select(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted)).List();
            entity.HasQuery("All").IgnoreQueryFilters().BypassVisibility("Visible").OrderBy(row => row.Id).Select(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted)).List();
        }
        void Configure(RepositoryCommandBuilder<ModelWriteFilterNote> command) => command.UseInMemoryModelQueryFilter(enabled);
        foreach (var name in new[] { "Rename", "StageRename", "RenameBypass" })
        {
            var command = entity.HasCommand(name).Where<ModelWriteFilterPatch>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, ModelWriteFilterPatch>(row => row.Title, (row, request) => request.Title)
                    .SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                .ExpectVersion<int, ModelWriteFilterPatch>(row => row.Version, (row, request) => request.ExpectedVersion);
            if (name == "RenameBypass") command.BypassVisibility("Visible");
            if (name == "StageRename") command.Stage();
            else command.Save().ReturnsSaved(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted));
            Configure(command);
        }
        foreach (var name in new[] { "Hide", "HideAndRead", "StageHide" })
        {
            var command = entity.HasCommand(name).Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Deleted, true));
            if (name == "StageHide") command.Stage();
            else if (name == "HideAndRead") command.Save().ReturnsSaved(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted));
            else command.Save().Returns(row => row.Title);
            Configure(command);
        }
        Configure(entity.HasCommand("Delete").Where<ModelWriteFilterPatch>((row, request) => row.Id == request.Id).TrackedDelete()
            .ExpectVersion<int, ModelWriteFilterPatch>(row => row.Version, (row, request) => request.ExpectedVersion).Save()
            .Returns(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted)));
        Configure(entity.HasCommand("StageDelete").Where<int>((row, id) => row.Id == id).TrackedDelete().Stage());
        foreach (var name in new[] { "RenameMany", "StageMany" })
        {
            var command = entity.HasCommand(name).PatchMany<ModelWriteFilterPatch, int, IReadOnlyList<ModelWriteFilterPatch>>(
                items => items, row => row.Id, item => item.Id, setters => setters.SetProperty(row => row.Title, (row, item) => item.Title)
                    .SetProperty(row => row.Version, row => row.Version + 1).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion), maximumItems: 3)
                .AllowWrite(row => row.Version);
            if (name == "StageMany") command.Stage(); else command.Save().ReturnsSaved(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted));
            Configure(command);
        }
        Configure(entity.HasCommand("DeleteMany").DeleteMany<ModelWriteFilterPatch, int, IReadOnlyList<ModelWriteFilterPatch>>(
            items => items, row => row.Id, item => item.Id, setters => setters.ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion), maximumItems: 3)
            .Save().Returns(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted)));
        foreach (var name in new[] { "Create", "CreateSaved" })
        {
            var command = entity.HasCommand(name).Create<ModelWriteFilterPatch>(request => new ModelWriteFilterNote
                { Id = request.Id, Title = request.Title, Version = 0, Deleted = request.Deleted, Hidden = request.Hidden }).AllowWrite(row => row.Id).AllowWrite(row => row.Version).Save();
            if (name == "CreateSaved") command.ReturnsSaved(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted));
            else command.Returns(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted));
            Configure(command);
        }
        Configure(entity.HasCommand("CreateMany").CreateMany<ModelWriteFilterPatch, IReadOnlyList<ModelWriteFilterPatch>>(
            items => items, item => new ModelWriteFilterNote { Id = item.Id, Title = item.Title, Version = 0, Deleted = item.Deleted, Hidden = item.Hidden }, maximumItems: 3)
            .AllowWrite(row => row.Id).AllowWrite(row => row.Version).Save().ReturnsSaved(row => new ModelWriteFilterCard(row.Id, row.Title, row.Version, row.Deleted)));
        Configure(entity.HasCommand("Update").Where<ModelWriteFilterPatch>((row, request) => row.Id == request.Id)
            .Update(setters => setters.SetProperty<string, ModelWriteFilterPatch>(row => row.Title, (row, request) => request.Title))
            .ExpectVersion<int, ModelWriteFilterPatch>(row => row.Version, (row, request) => request.ExpectedVersion).WithOutcome().SimulateImmediateWrites());
        Configure(entity.HasCommand("UpdateAll").Where(row => row.Id > 0).Update(setters => setters.SetProperty(row => row.Title, "bulk")).WithOutcome().SimulateImmediateWrites());
        Configure(entity.HasCommand("ImmediateHide").Where<int>((row, id) => row.Id == id).Update(setters => setters.SetProperty(row => row.Deleted, true)).WithOutcome().SimulateImmediateWrites());
        Configure(entity.HasCommand("ExecuteDelete").Where<ModelWriteFilterPatch>((row, request) => row.Id == request.Id).Delete()
            .ExpectVersion<int, ModelWriteFilterPatch>(row => row.Version, (row, request) => request.ExpectedVersion).WithOutcome().SimulateImmediateWrites());
        var audit = builder.Entity<MemoryWriteAudit>(); audit.ReadEnabled = false;
        audit.HasInMemoryStore(row => row.Id, row => new MemoryWriteAudit { Id = row.Id, Text = row.Text });
        audit.HasCommand("StageAudit").Create<ModelWriteFilterPatch>(request => new MemoryWriteAudit { Id = request.Id, Text = request.Title }).AllowWrite(row => row.Id).Stage();
        return builder;
    }

    internal static ModelWriteFilterNote[] Seeds() => [new() { Id = 1, Title = "one" }, new() { Id = 2, Title = "deleted", Deleted = true },
        new() { Id = 3, Title = "hidden", Hidden = true }, new() { Id = 4, Title = "four" }];
    internal static (object Note, object Audit, InMemoryWriteDataContext Data) Fixture(bool mapped = true, Assembly? assembly = null)
    {
        assembly ??= mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelWriteFilterContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped) Store<ModelWriteFilterSeed>(data).SeedRange(Seeds().Select(row => new ModelWriteFilterSeed(row.Id, row.Title, row.Version, row.Deleted, row.Hidden)));
        else Store<ModelWriteFilterNote>(data).SeedRange(Seeds());
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelWriteFilterNoteRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryWriteAuditRepositoryTestDouble")!, fixture)!, data);
    }
    internal static InMemoryStore<T, int> Store<T>(InMemoryWriteDataContext data) where T : class
        => (InMemoryStore<T, int>)data.GetType().GetProperty("ModelWriteFilterNoteStore")!.GetValue(data)!;
    internal static ModelWriteFilterSeed[] Rows(InMemoryWriteDataContext data, bool mapped)
        => mapped ? Store<ModelWriteFilterSeed>(data).Snapshot().OrderBy(row => row.Id).ToArray()
            : Store<ModelWriteFilterNote>(data).Snapshot().OrderBy(row => row.Id).Select(row => new ModelWriteFilterSeed(row.Id, row.Title, row.Version, row.Deleted, row.Hidden)).ToArray();
}
