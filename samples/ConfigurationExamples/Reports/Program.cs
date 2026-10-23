// Repository usage and executable assertions, not generator configuration.
// Start with Configuration/ReportingRepositoryDesigner.cs and README.md.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated;
using RonSijm.RepoGen.ConfigurationExamples.Models.Reporting;
using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Reports;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var sqlTimeouts = new CommandTimeoutRecorder();
        var options = new DbContextOptionsBuilder<ReportingContext>()
            .UseSqlite(connection, sqlite => sqlite.CommandTimeout(17)).AddInterceptors(sqlTimeouts).Options;
        await using (var seed = new ReportingContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Sales.AddRange(new Sale { Region = "North", Amount = 10 }, new Sale { Region = "North", Amount = 20 },
                new Sale { Region = "South", Amount = 40 });
            seed.RegionLabels.Add(new RegionLabel { Region = "North", Caption = "Northern branch" });
            await seed.SaveChangesAsync();
        }

        sqlTimeouts.Timeouts.Clear();

        var factory = new ReportingContextFactory(options);
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<ReportingContext>>(factory);
        services.AddScoped<IDbContextLeaseProvider<ReportingContext>, FactoryDbContextLeaseProvider<ReportingContext>>();
        services.AddReportingContextRepositories();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadSaleRepository>();
        var applicationPort = scope.ServiceProvider.GetRequiredService<ISalesReports>();
        if (!ReferenceEquals(repository, applicationPort))
        {
            throw new InvalidOperationException("The application port did not share the generated implementation.");
        }

        // Application-facing methods have no selectors, IQueryable, DbContext or write operations.
        var page = await repository.GetRegionTotalsAsync(new ReportRequest(2), 1, 10);
        if (page.TotalCount != 1 || page.Items[0].Region != "North" || page.Items[0].Total != 30)
        {
            throw new InvalidOperationException("Unexpected grouped report.");
        }

        await applicationPort.GetRegionTotalsAsync(new ReportRequest(0), 1, 10);
        var ranked = await applicationPort.GetRegionTotalsAsync(new ReportRequest(0) { RankByCount = true, LimitGroups = 1 }, 1, 10);
        if (ranked.TotalCount != 1 || ranked.Items[0].Region != "North")
        {
            throw new InvalidOperationException("The optional group ranking/limit did not run server-side.");
        }

        var skipped = await applicationPort.GetRegionTotalsAsync(new ReportRequest(0) { RankByCount = true, SkipGroups = 1 }, 1, 10);
        if (skipped.TotalCount != 1 || skipped.Items[0].Region != "South")
        {
            throw new InvalidOperationException("The optional group skip did not run server-side.");
        }

        var countSorted = await applicationPort.GetRegionTotalsAsync(new ReportRequest(0) { SortBy = "COUNT" }, 1, 10);
        if (countSorted.TotalCount != 2 || countSorted.Items[0].Region != "South")
        {
            throw new InvalidOperationException("The whitelisted grouped count sort did not run server-side.");
        }

        var totalSorted = await applicationPort.GetRegionTotalsAsync(new ReportRequest(0) { SortBy = "total", Descending = true }, 1, 10);
        if (totalSorted.Items[0].Region != "South")
        {
            throw new InvalidOperationException("The whitelisted descending total sort failed.");
        }

        try
        {
            await applicationPort.GetRegionTotalsAsync(new ReportRequest(0) { SortBy = "total; DROP TABLE Sales" }, 1, 10);
            throw new InvalidOperationException("An unknown sort field was accepted.");
        }
        catch (ArgumentException)
        {
            /* Unknown fields are rejected before acquiring a context. */
        }

        var regionAmounts = await applicationPort.GetTotalsByRegionAsync(new ReportAmountRequest(2, " branch"));
        if (regionAmounts.Count != 2 || regionAmounts["North branch"] != 60 || regionAmounts["South branch"] != 80 ||
            await applicationPort.GetScaledAmountAsync(new ReportAmountRequest(2)) != 140 ||
            await applicationPort.GetScaledAmountAsync(new ReportAmountRequest(3, Region: "North")) != 90)
        {
            throw new InvalidOperationException("Request-bound dictionary/aggregate terminals did not apply their designer mappings.");
        }

        try
        {
            await applicationPort.GetScaledAmountAsync(new ReportAmountRequest(-1));
            throw new InvalidOperationException("The terminal request guard was bypassed.");
        }
        catch (ArgumentException)
        {
            /* Input guards run before owned context acquisition. */
        }

        var cursorRequest = new SalesCursorRequest(Region: "North");
        var firstSales = await applicationPort.GetSalesCursorAsync(cursorRequest, new Abstractions.CursorPageRequest<SalesCursor>(1));
        var nextSales = await applicationPort.GetSalesCursorAsync(cursorRequest, new Abstractions.CursorPageRequest<SalesCursor>(firstSales.NextCursor!, 1));
        var lastSales = await applicationPort.GetSalesCursorAsync(cursorRequest with { Descending = true }, new Abstractions.CursorPageRequest<SalesCursor>(1));
        var previousSales = await applicationPort.GetSalesCursorAsync(cursorRequest with { Descending = true }, new Abstractions.CursorPageRequest<SalesCursor>(lastSales.NextCursor!, 1));
        if (firstSales.Items.Single().Amount != 10 || !firstSales.HasMore || nextSales.Items.Single().Amount != 20 || nextSales.HasMore ||
            lastSales.Items.Single().Amount != 20 || previousSales.Items.Single().Amount != 10)
        {
            throw new InvalidOperationException("Request-aware cursor paging did not preserve the designed direction and seek.");
        }

        try
        {
            await applicationPort.GetSalesCursorAsync(cursorRequest with { Region = "South" }, new Abstractions.CursorPageRequest<SalesCursor>(firstSales.NextCursor!, 1));
            throw new InvalidOperationException("A cursor from another report scope was accepted.");
        }
        catch (ArgumentException)
        {
            /* A continuation guard rejects the mismatch before context acquisition. */
        }

        var details = await repository.GetSalesWithLabelsAsync();
        if (details.Count != 3 || details[0].RegionCaption != "Northern branch" || details[2].RegionCaption is not null)
        {
            throw new InvalidOperationException("The configured left join lost a row or its nullable label.");
        }

        if (factory.Contexts.Count != 14 || factory.Contexts.Any(static context => context.DisposeCount != 1))
        {
            throw new InvalidOperationException("An operation context lease was not acquired/disposed exactly once.");
        }

        if (sqlTimeouts.Timeouts.Count != 20 || sqlTimeouts.Timeouts.Take(12).Any(static timeout => timeout != 3) ||
            sqlTimeouts.Timeouts.Skip(12).Take(7).Any(static timeout => timeout != 5) || sqlTimeouts.Timeouts[^1] != 17)
        {
            throw new InvalidOperationException("Configured query timeouts did not apply to every SQL command.");
        }

        Console.WriteLine("Reports: EF-free read port, grouped page, optional ranking/limits, whitelisted sorting, request-bound terminals/cursor paging and guards, configured left join, designer timeouts and async context leases passed.");
    }
}
