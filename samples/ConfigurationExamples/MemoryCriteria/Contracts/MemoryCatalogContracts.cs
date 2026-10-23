using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCatalog;

public readonly record struct CatalogPartition(string Name);
public sealed record CatalogCriteria(CatalogPartition Partition, string? Search = null, int MinimumPrice = 0,
    IEnumerable<int>? Ids = null, string? SortBy = null, bool Descending = false);
public sealed record CatalogLookup(CatalogPartition Partition, int Id);
public sealed record CatalogItem(int Id, string Name, string Category, int Price)
{
    public CatalogItem() : this(0, "", "", 0) { }
}
public sealed record CatalogTotals(string Category, int Count, int Total)
{
    public CatalogTotals() : this("", 0, 0) { }
}

// Public POCO, not an EF API. The independent Model project maps this entity-shaped fixture row.
public sealed class CatalogEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int Price { get; set; }
    public bool Hidden { get; set; }
}

public interface ICatalogReads
{
    Task<CatalogItem?> FindAsync(CatalogLookup request, CancellationToken cancellationToken = default);
    Task<PagedResult<CatalogItem>> SearchAsync(CatalogCriteria criteria, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogTotals>> TotalsAsync(CatalogCriteria criteria, CancellationToken cancellationToken = default);
    IAsyncEnumerable<CatalogItem> StreamAsync(CatalogCriteria criteria, CancellationToken cancellationToken = default);
}
