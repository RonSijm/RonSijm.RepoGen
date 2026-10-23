// This example's application contracts; compiled by the shared EF-free Contracts project.
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public sealed record ReportRequest(int MinimumCount)
{
    public bool RankByCount { get; init; }
    public string? SortBy { get; init; }
    public bool Descending { get; init; }
    public int? SkipGroups { get; init; }
    public int? LimitGroups { get; init; }
}

public sealed record RegionTotal(string Region, int Count, int Total);

public sealed record ReportAmountRequest(int Multiplier = 1, string LabelSuffix = "", string? Region = null);

public sealed record SaleDetails(int Id, string Region, int Amount, string? RegionCaption);

public sealed record SalesCursorRequest(bool Descending = false, string? Region = null);

public sealed record SalesCursor(int Id, bool Descending, string? Region);

public sealed class SaleCursorRow
{
    public int Id { get; init; }
    public string Region { get; init; } = string.Empty;
    public int Amount { get; init; }
}

// This application-owned port has no EF or entity reference; the designer binds it explicitly.
public interface ISalesReports
{
    Task<PagedResult<RegionTotal>> GetRegionTotalsAsync(
        ReportRequest request, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SaleDetails>> GetSalesWithLabelsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, int>> GetTotalsByRegionAsync(ReportAmountRequest request, CancellationToken cancellationToken = default);
    Task<int> GetScaledAmountAsync(ReportAmountRequest request, CancellationToken cancellationToken = default);

    Task<CursorPagedResult<SaleCursorRow, SalesCursor>> GetSalesCursorAsync(
        SalesCursorRequest request, CursorPageRequest<SalesCursor> cursorPage, CancellationToken cancellationToken = default);
}
