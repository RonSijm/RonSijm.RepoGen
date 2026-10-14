using System.Reflection;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class FilteredNavigationTestHarness
{
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    private static Assembly Compile(bool mapped)
    {
        using var context = ModelFilterTestHarness.Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }

    internal static RepositoryModelBuilder Base(bool mapped = true, bool navigationFilters = true, bool strict = true, bool queries = true)
    {
        var builder = ModelFilterTestHarness.Base(mapped, strict: strict, queries: queries);
        builder.Entity<ModelFilterRow>().UseInMemoryModelQueryFilter(includeNavigations: navigationFilters).HasInMemoryNavigation(row => row.Folder);
        builder.Entity<ModelFilterFolder>().UseInMemoryModelQueryFilter(includeNavigations: navigationFilters).HasInMemoryNavigation(folder => folder.Rows);
        return builder;
    }

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool navigationFilters = true, bool strict = true, bool queries = true)
    {
        var builder = Base(mapped, navigationFilters, strict, queries);
        var row = builder.Entity<ModelFilterRow>();
        row.HasProjection<FilteredNavigationCard>(item => new FilteredNavigationCard(item.Id, item.Title, item.Folder == null ? null : item.Folder.Id));
        row.HasQuery("Cards").OrderBy(item => item.Id).ProjectTo<FilteredNavigationCard>().List();
        row.HasQuery("All").IgnoreQueryFilters().OrderBy(item => item.Id).ProjectTo<FilteredNavigationCard>().List();
        row.HasQuery("VisibleFolder").Where(item => item.Folder != null).Select(item => item.Id).List();
        row.HasQuery("From").From(sources => sources.Set<ModelFilterRow>().OrderBy(item => item.Id)
            .Select(item => new FilteredNavigationCard(item.Id, item.Title, item.Folder == null ? null : item.Folder.Id))).List();
        row.HasQuery("Joined").From(sources => sources.Set<ModelFilterRow>().Join(sources.Set<ModelFilterFolder>(),
            item => item.FolderId, folder => (int?)folder.Id, (item, folder) => new { item, folder })
            .OrderBy(pair => pair.item.Id).Select(pair => pair.item.Folder == null ? -1 : pair.item.Folder.Id)).List();
        row.HasQuery("Page").OrderBy(item => item.Id).ProjectTo<FilteredNavigationCard>().Paged();
        row.HasQuery("Stream").OrderBy(item => item.Id).ProjectTo<FilteredNavigationCard>().Stream();
        var folder = builder.Entity<ModelFilterFolder>();
        folder.HasProjection<FilteredNavigationFolderCard>(item => new FilteredNavigationFolderCard(item.Id, item.Rows.Count,
            item.Rows.OrderBy(child => child.Id).Select(child => child.Title).ToList()));
        folder.HasQuery("Cards").OrderBy(item => item.Id).ProjectTo<FilteredNavigationFolderCard>().List();
        folder.HasQuery("Loaded").OrderBy(item => item.Id).Load(load => load.Include(item => item.Rows)).ProjectTo<FilteredNavigationFolderCard>().List();
        folder.HasQuery("All").IgnoreQueryFilters().OrderBy(item => item.Id).ProjectTo<FilteredNavigationFolderCard>().List();
        folder.HasQuery("Any").Where<string>((item, title) => item.Rows.Any(child => child.Title == title)).Select(item => item.Id).List();
        folder.HasQuery("Page").OrderBy(item => item.Id).ProjectTo<FilteredNavigationFolderCard>().Paged();
        folder.HasQuery("Stream").OrderBy(item => item.Id).ProjectTo<FilteredNavigationFolderCard>().Stream();
        return builder;
    }

    internal static (object Rows, object Folders, InMemoryDataContext Data) Fixture(bool mapped = true, Assembly? assembly = null)
    {
        assembly ??= mapped ? Mapped.Value : Entities.Value;
        var (rows, data) = ModelFilterTestHarness.Fixture(mapped, assembly);
        var fixture = rows.GetType().GetProperty("Fixture")!.GetValue(rows)!;
        var folders = Activator.CreateInstance(assembly.GetType(ModelFilterTestHarness.Namespace + ".Testing.ModelFilterFolderRepositoryTestDouble")!, fixture)!;
        return (rows, folders, data);
    }
}
