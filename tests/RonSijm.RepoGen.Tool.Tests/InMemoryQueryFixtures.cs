using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed record PortableView(int Id, string Name, string Category, int Price)
{
    public PortableView() : this(0, "", "", 0) { }
}
public sealed record PortableReport(string Category, int Count, int Total, int Maximum)
{
    public PortableReport() : this("", 0, 0, 0) { }
}
public sealed record PortableCriteria(string? Term = null, int Minimum = 0, IEnumerable<int>? Ids = null, string? SortBy = null,
    bool Descending = false, bool Limited = false, int Limit = 2);
public interface IPortableReadPort
{
    Task<PortableView?> FindAsync(int lookup, CancellationToken requestCancellation = default);
}

public sealed class PortableRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int Price { get; set; }
    public bool Hidden { get; set; }
    public int? DetailId { get; set; }
    public PortableDetail? Detail { get; set; }
}
public sealed class PortableDetail
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
}
public sealed class PortableReadContext(DbContextOptions<PortableReadContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<PortableRow>();
}
public sealed class PortableFilteredContext(DbContextOptions<PortableFilteredContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<PortableRow>().HasQueryFilter(row => !row.Hidden);
}

public sealed class PortableReadRepositoryDesigner(bool enabled = true, bool strict = false) : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.Portable.Contracts")
            .UseGeneratedNamespace("Generated.Portable.Persistence").UseScriptedTestDoubles("Generated.Portable.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestQueries(enabled).RequirePortableTestQueries(strict).GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<PortableRow>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<IPortableReadPort>();
            entity.HasInMemoryStore(row => row.Id, row => new PortableRow { Id = row.Id, Name = row.Name, Category = row.Category, Price = row.Price, Hidden = row.Hidden, DetailId = row.DetailId });
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasDefaultSort(row => row.Id).Descending();
            entity.HasSortableFields(row => row.Name, row => row.Price);
            entity.HasProjection(row => new PortableView { Id = row.Id, Name = row.Name, Category = row.Category, Price = row.Price });
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id).ProjectTo<PortableView>().SingleOrDefault();
            entity.HasQuery("Require").Where<int>((row, id) => row.Id == id).ProjectTo<PortableView>().Single();
            entity.HasQuery("Default").ProjectTo<PortableView>().List();
            entity.HasQuery("Unordered").UseDefaultSort(false).ProjectTo<PortableView>().List();
            entity.HasQuery("AllIncludingHidden").BypassVisibility("Visible").ProjectTo<PortableView>().List();
            entity.HasQuery("Count").Count();
            entity.HasQuery("Any").Where<int>((row, minimum) => row.Price >= minimum).Exists();
            entity.HasQuery("Latest").OrderByDescending(row => row.Price).ThenBy(row => row.Id).ProjectTo<PortableView>().FirstOrDefault();
            entity.HasQuery("FirstValue").Where<int>((row, id) => row.Id == id).Select(row => row.Price).FirstOrDefault();
            entity.HasQuery("SimpleValue").Where<int>((row, id) => row.Id == id).ProjectTo(row => row.Price).SingleOrDefault();
            entity.HasQuery("AnnotatedValue").Where<int>((row, id) => row.Id == id).ProjectTo(row => row.Price)
                .ResultNullability<int>(_ => { }).SingleOrDefault();
            entity.HasQuery("RequireOne").ProjectTo<PortableView>().SingleOrDefault();

            var search = entity.HasFragment<PortableCriteria>("SearchText", (row, criteria) => row.Name.Contains(criteria.Term!));
            entity.HasQuery("Search").Validate<PortableCriteria>(criteria => criteria.Minimum >= 0, "Minimum cannot be negative.")
                .Where<PortableCriteria>((row, criteria) => row.Price >= criteria.Minimum)
                .UseIf<PortableCriteria>(criteria => !string.IsNullOrEmpty(criteria.Term), search)
                .WhereIn<int, PortableCriteria>(row => row.Id, criteria => criteria.Ids, options => options.NullInput(RepositoryMembershipInputBehavior.IgnoreFilter).MaximumCount(10).Distinct())
                .ProjectTo<PortableView>().Results<PortableView>()
                .OrderByField<PortableCriteria>(criteria => criteria.SortBy, criteria => criteria.Descending,
                    fields => fields.Field("name", row => row.Name).Field("price", row => row.Price).UseDefault("name"))
                .ThenBy(row => row.Id).TakeIf<PortableCriteria>(criteria => criteria.Limited, criteria => criteria.Limit).List();
            entity.HasQuery("Dynamic").AllowSorting(row => row.Name, row => row.Price).ProjectTo<PortableView>().List();
            entity.HasQuery("Page").ProjectTo<PortableView>().Results<PortableView>().OrderBy(row => row.Id).Paged();
            entity.HasQuery("Slice").ProjectTo<PortableView>().Results<PortableView>().OrderBy(row => row.Id).Slice();
            entity.HasQuery("Cursor").OrderBy(row => row.Id).ProjectTo<PortableView>().CursorPaged<int>((row, cursor) => row.Id > cursor, row => row.Id);
            entity.HasQuery("ResultCursor").ProjectTo<PortableView>().Results<PortableView>().OrderBy(row => row.Id)
                .CursorPaged<int>((row, cursor) => row.Id > cursor, row => row.Id);
            entity.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<PortableView>().Stream();
            entity.HasQuery("Dictionary").ProjectTo<PortableView>().Results<PortableView>().Dictionary(row => row.Id, row => row.Name);
            entity.HasQuery("DuplicateDictionary").ProjectTo<PortableView>().Results<PortableView>().Dictionary(row => row.Category, row => row.Id);
            entity.HasQuery("KeepFirst").ProjectTo<PortableView>().Results<PortableView>().OrderBy(row => row.Id)
                .Dictionary(row => row.Category, row => row.Id, RepositoryDuplicateKeyBehavior.KeepFirst);
            entity.HasQuery("KeepLast").ProjectTo<PortableView>().Results<PortableView>().OrderBy(row => row.Id)
                .Dictionary(row => row.Category, row => row.Id, RepositoryDuplicateKeyBehavior.KeepLast);
            entity.HasQuery("Lookup").ProjectTo<PortableView>().Results<PortableView>().OrderBy(row => row.Id).Lookup(row => row.Category, row => row.Name);
            entity.HasQuery("Categories").Select(row => row.Category).Distinct().OrderBy(row => row).List();
            entity.HasQuery("Report").GroupBy(row => row.Category, group => new PortableReport { Category = group.Key, Count = group.Count(), Total = group.Sum(row => row.Price), Maximum = group.Max(row => row.Price) })
                .Results<PortableReport>().Where(row => row.Total >= 10).OrderBy(row => row.Category).List();
            entity.HasQuery("ResultGroups").Select(row => new { row.Category, row.Price })
                .GroupBy(row => row.Category, group => new PortableReport { Category = group.Key, Count = group.Count(), Total = group.Sum(row => row.Price), Maximum = group.Max(row => row.Price) })
                .OrderBy(row => row.Category).List();
            entity.HasQuery("Sum").Results<PortableRow>().Sum(row => row.Price);
            entity.HasQuery("Average").Results<PortableRow>().Average(row => row.Price);
            entity.HasQuery("Max").Results<PortableRow>().Max(row => row.Price);
            entity.HasQuery("Min").Results<PortableRow>().Min(row => row.Price);
        });
    }
}
