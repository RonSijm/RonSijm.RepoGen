// Paired with ../Model/ReportingContext.cs by ../../GenerationRunner/Program.cs.
// API: ../../Contracts/Generated/ReportingRepositories.g.cs; implementation: ../Generated/Repositories.g.cs.
// See ../README.md for the declaration-to-output map; the consumer does not run this designer.
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.Reporting;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class ReportingRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated")
            .UseAsyncContextLeases()
            .UseCommandTimeout(8)
            .GenerateOnlyConfiguredEntities());

        modelBuilder.Entity<Sale>(entity =>
        {
            // Closed read operations default to no-tracking; services see no tracking controls.
            entity.WriteEnabled = false;
            entity.ProjectionEnabled = false;
            entity.BindReadPort<ISalesReports>();
            // Optional design-time SQL policies: operation overrides entity, then model defaults.
            entity.UseCommandTimeout(5);
            entity.HasQuery("GetRegionTotals")
                .UseDefaultSort(false)
                .Results<Sale>()
                .GroupBy(sale => sale.Region, group => new
                {
                    Region = group.Key,
                    Count = group.Count(),
                    Total = group.Sum(sale => sale.Amount)
                })
                .WhereIf<ReportRequest>(request => request.MinimumCount > 0,
                    (total, request) => total.Count >= request.MinimumCount)
                .OrderByDescending(total => total.Total)
                // The normal order remains the fallback; disabled ranking adds no sort expression to SQL.
                .OrderByDescendingIf((ReportRequest request) => request.RankByCount, total => total.Count)
                // Public aliases are fixed here; a supplied field takes precedence over the fallback/ranking.
                .OrderByField((ReportRequest request) => request.SortBy, preferences => preferences.Descending,
                    fields => fields.Field("region", total => total.Region)
                        .Field("count", total => total.Count)
                        .Field("total", total => total.Total)
                        .PreserveOrderWhenUnspecified())
                .ThenBy(total => total.Region)
                // Counts are evaluated only when enabled, using the condition's canonical request name.
                .SkipIf(request => request.SkipGroups.HasValue, (ReportRequest limits) => limits.SkipGroups.GetValueOrDefault())
                .TakeIf(request => request.LimitGroups.HasValue, (ReportRequest limits) => limits.LimitGroups.GetValueOrDefault())
                .Select(total => new RegionTotal(total.Region, total.Count, total.Total))
                .UseCommandTimeout(3)
                .Paged();

            // Terminal mappings can share the same typed request without adding service-side projections.
            entity.HasQuery("GetTotalsByRegion")
                .Validate<ReportAmountRequest>(request => request.Multiplier >= 0, "Multiplier must be non-negative.")
                .Results<Sale>()
                .GroupBy(sale => sale.Region, group => new { Region = group.Key, Total = group.Sum(sale => sale.Amount) })
                .Dictionary<string, int, ReportAmountRequest>((total, request) => total.Region + request.LabelSuffix,
                    (total, formatting) => total.Total * formatting.Multiplier);
            entity.HasQuery("GetScaledAmount")
                .Validate<ReportAmountRequest>(request => request.Multiplier >= 0, "Multiplier must be non-negative.")
                .WhereIf<ReportAmountRequest>(request => request.Region != null, (sale, request) => sale.Region == request.Region)
                .Select(sale => sale.Amount)
                .Sum<ReportAmountRequest>((amount, request) => amount * request.Multiplier);

            // Direction and scope belong to this designed operation, not service-side query extensions.
            entity.HasQuery("GetSalesCursor")
                .WhereIf<SalesCursorRequest>(request => request.Region != null, (sale, request) => sale.Region == request.Region)
                .Select(sale => new SaleCursorRow { Id = sale.Id, Region = sale.Region, Amount = sale.Amount })
                .OrderBy(sale => sale.Id)
                .OrderByDescendingIf((SalesCursorRequest request) => request.Descending, sale => sale.Id)
                .CursorPaged<SalesCursor, SalesCursorRequest>(
                    (sale, cursor, request) => request.Descending ? sale.Id < cursor.Id : sale.Id > cursor.Id,
                    (sale, formatting) => new SalesCursor(sale.Id, formatting.Descending, formatting.Region))
                .ValidateCursor<SalesCursor, SalesCursorRequest>(
                    (cursor, request) => cursor.Descending == request.Descending && cursor.Region == request.Region,
                    "This cursor belongs to a different region or direction.");

            // The second set is not exposed as a repository and does not need an EF navigation.
            entity.HasQuery("GetSalesWithLabels")
                .From(sources => from sale in sources.Set<Sale>()
                                 join label in sources.Set<RegionLabel>() on sale.Region equals label.Region into labels
                                 from label in labels.DefaultIfEmpty()
                                 orderby sale.Id
                                 select new SaleDetails(sale.Id, sale.Region, sale.Amount, label == null ? null : label.Caption))
                // Explicit null bypasses inherited timeouts and retains the context/provider setting.
                .UseCommandTimeout(null)
                .List();
        });
    }
}
