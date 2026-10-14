using System.Reflection;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class SavedGraphFilterTestHarness
{
    internal const string Namespace = "Generated.SavedGraphFilters";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    private static Assembly Compile(bool mapped)
    {
        using var context = ModelFilterTestHarness.Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }

    internal static RepositoryModelBuilder Base(bool mapped = true, bool relatedFilters = true, bool strict = true, bool backend = true, bool queries = true)
    {
        var builder = ModelFilterTestHarness.Base(mapped, enabled: false, strict: false, queries: queries);
        builder.Configure(options => options.UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestCommands(backend).RequirePortableTestCommands(strict && backend));
        var row = builder.Entity<ModelFilterRow>(); row.WriteEnabled = true; row.HasInMemoryNavigation(item => item.Folder);
        var folder = builder.Entity<ModelFilterFolder>(); folder.WriteEnabled = true; folder.HasInMemoryNavigation(item => item.Rows);
        // A command owns its root permission, regardless of these entity read settings.
        // Both definitions independently act as related datasets in another command's result.
        row.UseInMemoryModelQueryFilter(includeNavigations: relatedFilters);
        folder.UseInMemoryModelQueryFilter(includeNavigations: relatedFilters);
        if (mapped)
        {
            row.MapInMemoryWrites<ModelFilterRowSeed>((item, saved) => new(item.Id, item.Title, item.Deleted, item.Hidden, item.FolderId));
            folder.MapInMemoryWrites<ModelFilterFolderSeed>((item, saved) => new(item.Id, item.Archived));
        }
        return builder;
    }

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool graphFilters = true, bool relatedFilters = true, bool strict = true,
        bool backend = true, bool queries = true)
    {
        var builder = Base(mapped, relatedFilters, strict, backend, queries);
        var row = builder.Entity<ModelFilterRow>();
        row.HasCommand("RenameGraph").Where<SavedGraphFilterRequest>((item, request) => item.Id == request.Id)
            .Patch(setters => setters.SetProperty<string, SavedGraphFilterRequest>(item => item.Title, (item, request) => request.Title))
            .UseInMemoryModelQueryFilter(includeNavigations: graphFilters).Save()
            .ReturnsSaved(item => new SavedGraphFilterCollectionCard(item.Id, item.Title, item.Folder == null ? null : item.Folder.Id,
                item.Folder == null ? 0 : item.Folder.Rows.Count));
        foreach (var name in new[] { "Rename", "RenameBypass" })
        {
            var command = row.HasCommand(name).Where<SavedGraphFilterRequest>((item, request) => item.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, SavedGraphFilterRequest>(item => item.Title, (item, request) => request.Title))
                .UseInMemoryModelQueryFilter(includeNavigations: graphFilters).Save()
                .ReturnsSaved(item => new FilteredNavigationCard(item.Id, item.Title, item.Folder == null ? null : item.Folder.Id));
            if (name == "RenameBypass") command.BypassVisibility("Visible");
        }
        row.HasCommand("RenameMany").PatchMany<SavedGraphFilterRequest, int, IReadOnlyList<SavedGraphFilterRequest>>(
            items => items, item => item.Id, input => input.Id, setters => setters.SetProperty(item => item.Title, (item, input) => input.Title), maximumItems: 3)
            .UseInMemoryModelQueryFilter(includeNavigations: graphFilters).Save()
            .ReturnsSaved(item => new FilteredNavigationCard(item.Id, item.Title, item.Folder == null ? null : item.Folder.Id));
        row.HasCommand("Archive").Where<int>((item, id) => item.Id == id).Patch(setters => setters.SetProperty(item => item.Deleted, true))
            .UseInMemoryModelQueryFilter(includeNavigations: graphFilters).Save()
            .ReturnsSaved(item => new FilteredNavigationCard(item.Id, item.Title, item.Folder == null ? null : item.Folder.Id));
        row.HasCommand("ArchiveMany").PatchMany<SavedGraphFilterRequest, int, IReadOnlyList<SavedGraphFilterRequest>>(
            items => items, item => item.Id, input => input.Id, setters => setters.SetProperty(item => item.Deleted, true), maximumItems: 3)
            .UseInMemoryModelQueryFilter(includeNavigations: graphFilters).Save()
            .ReturnsSaved(item => new FilteredNavigationCard(item.Id, item.Title, item.Folder == null ? null : item.Folder.Id));
        row.HasCommand("StageRename").Where<SavedGraphFilterRequest>((item, request) => item.Id == request.Id)
            .Patch(setters => setters.SetProperty<string, SavedGraphFilterRequest>(item => item.Title, (item, request) => request.Title))
            .UseInMemoryModelQueryFilter().Stage();
        row.HasCommand("Create").Create<SavedGraphFilterRequest>(request => new ModelFilterRow
            { Id = request.Id, Title = request.Title, FolderId = request.FolderId, Deleted = request.Deleted, Hidden = request.Hidden })
            .AllowWrite(item => item.Id).AllowWrite(item => item.FolderId).UseInMemoryModelQueryFilter(includeNavigations: graphFilters).Save()
            .ReturnsSaved(item => new FilteredNavigationCard(item.Id, item.Title, item.Folder == null ? null : item.Folder.Id));
        row.HasCommand("CreateMany").CreateMany<SavedGraphFilterRequest, IReadOnlyList<SavedGraphFilterRequest>>(items => items, input => new ModelFilterRow
            { Id = input.Id, Title = input.Title, FolderId = input.FolderId, Deleted = input.Deleted, Hidden = input.Hidden }, maximumItems: 3)
            .AllowWrite(item => item.Id).AllowWrite(item => item.FolderId).UseInMemoryModelQueryFilter(includeNavigations: graphFilters).Save()
            .ReturnsSaved(item => new FilteredNavigationCard(item.Id, item.Title, item.Folder == null ? null : item.Folder.Id));
        row.HasQuery("All").IgnoreQueryFilters().BypassVisibility("Visible").OrderBy(item => item.Id)
            .Select(item => new ModelFilterRowSeed(item.Id, item.Title, item.Deleted, item.Hidden, item.FolderId)).List();
        var folder = builder.Entity<ModelFilterFolder>();
        folder.HasCommand("StageArchive").Where<int>((item, id) => item.Id == id).Patch(setters => setters.SetProperty(item => item.Archived, true))
            .UseInMemoryModelQueryFilter().Stage();
        folder.HasCommand("Create").Create<SavedGraphFilterFolderInput>(input => new ModelFilterFolder { Id = input.Id, Archived = input.Archived })
            .AllowWrite(item => item.Id).UseInMemoryModelQueryFilter(includeNavigations: graphFilters).Save()
            .ReturnsSaved(item => new FilteredNavigationFolderCard(item.Id, item.Rows.Count, item.Rows.OrderBy(child => child.Id).Select(child => child.Title).ToList()));
        return builder;
    }

    internal static (object Rows, object Folders, InMemoryWriteDataContext Data) Fixture(bool mapped = true, Assembly? assembly = null)
    {
        assembly ??= mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelFilterContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped)
        {
            ModelFilterTestHarness.Store<ModelFilterRowSeed>(data, "ModelFilterRow").SeedRange(ModelFilterTestHarness.Seeds().Select(item => new ModelFilterRowSeed(item.Id, item.Title, item.Deleted, item.Hidden, item.FolderId)));
            ModelFilterTestHarness.Store<ModelFilterFolderSeed>(data, "ModelFilterFolder").SeedRange(ModelFilterTestHarness.Folders().Select(item => new ModelFilterFolderSeed(item.Id, item.Archived)));
        }
        else
        {
            ModelFilterTestHarness.Store<ModelFilterRow>(data, "ModelFilterRow").SeedRange(ModelFilterTestHarness.Seeds());
            ModelFilterTestHarness.Store<ModelFilterFolder>(data, "ModelFilterFolder").SeedRange(ModelFilterTestHarness.Folders());
        }
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelFilterRowRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelFilterFolderRepositoryTestDouble")!, fixture)!, data);
    }
    internal static Task<T> Call<T>(object repository, string name, params object?[] inputs)
        => MemoryWriteTestHarness.Invoke<T>(repository, name + "Async", inputs);
}
