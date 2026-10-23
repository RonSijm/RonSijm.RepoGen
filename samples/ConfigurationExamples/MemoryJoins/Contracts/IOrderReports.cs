using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.OrderReports;

public interface IOrderReports
{
    Task<IReadOnlyList<OrderAccountRow>> JoinedAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderAccountRow>> LeftJoinedAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> UnmatchedAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<AccountOrderTotal>> TotalsAsync(OrderReportCriteria criteria, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> DirectoryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> IdentifiersAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<OrderAccountRow> StreamAsync(CancellationToken cancellationToken = default);
}
