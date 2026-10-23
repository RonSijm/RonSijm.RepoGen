using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProxyReadRepositoryDesigner(bool mapping = true, bool enabled = true, bool keyed = false) : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.Proxies.Contracts")
            .UseGeneratedNamespace("Generated.Proxies.Persistence").UseScriptedTestDoubles("Generated.Proxies.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestQueries(enabled).RequirePortableTestQueries(mapping && enabled).GenerateOnlyConfiguredEntities());
        if (keyed) modelBuilder.Configure(options => options.UseKeyedContextLeases<string>());
        modelBuilder.Entity<ProxyReadDepartment>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<ProxyReadDepartmentData, int>(row => row.Key, row => new ProxyReadDepartmentData(row.Key, row.Caption, row.Archived));
            if (mapping) entity.MapInMemoryEntity<ProxyReadDepartmentData>(row => new ProxyReadDepartment { Id = row.Key, Name = row.Caption, Hidden = row.Archived });
            entity.HasVisibilityPolicy("Departments", row => !row.Hidden);
        });
        modelBuilder.Entity<ProxyReadEntity>(entity =>
        {
            entity.WriteEnabled = false;
            Store(entity, mapping);
            entity.HasVisibilityPolicy("Visible", row => row.Visible);
            entity.HasDefaultSort(row => row.Name);
            entity.HasProjection(row => new ProxyReadView(row.Id, row.Name, row.Category, row.Score, null));
            RepositoryQueryBuilder<ProxyReadEntity> Query(string name)
            {
                var query = entity.HasQuery(name);
                if (keyed) query.UseContextKey<string, string>(contextKey => contextKey);
                return query;
            }
            Query("All").ProjectTo<ProxyReadView>().List();
            Query("Find").Where<int>((row, id) => row.Id == id).ProjectTo<ProxyReadView>().SingleOrDefault();
            Query("Count").Count();
            Query("Any").Where<int>((row, minimum) => row.Score >= minimum).Exists();
            Query("Names").Select(row => row.Name).Distinct().OrderBy(name => name).List();
            Query("Search").Where<string>((row, term) => row.Name.StartsWith(term)).ProjectTo<ProxyReadView>().List();
            Query("Dynamic").AllowSorting(row => row.Name, row => row.Score).ProjectTo<ProxyReadView>().List();
            Query("Page").OrderBy(row => row.Id).ProjectTo<ProxyReadView>().Paged();
            Query("Cursor").OrderBy(row => row.Id).ProjectTo<ProxyReadView>().CursorPaged<int>((row, cursor) => row.Id > cursor, row => row.Id);
            Query("Dictionary").Results<ProxyReadEntity>().Dictionary(row => row.Id, row => row.Name);
            Query("Sum").Results<ProxyReadEntity>().Sum(row => row.Score);
            Query("Report").GroupBy(row => row.Category, group => new ProxyReadTotal { Category = group.Key, Count = group.Count(), Score = group.Sum(row => row.Score) })
                .Results<ProxyReadTotal>().OrderBy(row => row.Category).List();
            Query("Stream").OrderBy(row => row.Id).ProjectTo<ProxyReadView>().Stream();
            Query("Bytes").OrderBy(row => row.Id).Select(row => row.Stamp).List();
            Query("Missing").RequireTestOverride("Unmapped persistence fields are deliberately not fixture data.").Select(row => row.Unmapped).List();
            Query("Join").From(sources => sources.Set<ProxyReadEntity>().Join(sources.Set<ProxyReadDepartment>(), row => row.DepartmentId, department => department.Id,
                    (row, department) => new ProxyReadView { Id = row.Id, Name = row.Name, Category = row.Category, Score = row.Score, Department = department.Name }))
                .OrderBy(row => row.Id).List();
            Query("Left").From(sources => sources.Set<ProxyReadEntity>().GroupJoin(sources.Set<ProxyReadDepartment>(), row => row.DepartmentId, department => department.Id,
                    (row, departments) => new { row, departments }).SelectMany(pair => pair.departments.DefaultIfEmpty(),
                    (pair, department) => new ProxyReadView { Id = pair.row.Id, Name = pair.row.Name, Category = pair.row.Category, Score = pair.row.Score, Department = department == null ? null : department.Name }))
                .OrderBy(row => row.Id).List();
            Query("Unmatched").From(sources => sources.Set<ProxyReadEntity>().Where(row => !sources.Set<ProxyReadDepartment>().Any(department => department.Id == row.DepartmentId))
                .Select(row => row.Id)).OrderBy(id => id).List();
            Query("Union").From(sources => sources.Set<ProxyReadEntity>().Select(row => row.Name).Union(sources.Set<ProxyReadDepartment>().Select(row => row.Name)))
                .OrderBy(name => name).List();
        });
    }

    internal static RepositoryEntityBuilder<ProxyReadEntity> Store(RepositoryEntityBuilder<ProxyReadEntity> entity, bool mapping = true)
    {
        entity.HasInMemoryStore<ProxyReadData, int>(row => row.Key, row => new ProxyReadData
            { Key = row.Key, Caption = row.Caption, Group = row.Group, RawScore = row.RawScore, Archived = row.Archived, DepartmentKey = row.DepartmentKey, Stamp = row.Stamp.ToArray() });
        if (mapping) entity.MapInMemoryEntity<ProxyReadData>(row => new ProxyReadEntity
            { Id = row.Key, Name = row.Caption.ToUpperInvariant(), Category = row.Group, Score = row.RawScore * 2, Visible = !row.Archived, DepartmentId = row.DepartmentKey, Stamp = row.Stamp });
        return entity;
    }
}
