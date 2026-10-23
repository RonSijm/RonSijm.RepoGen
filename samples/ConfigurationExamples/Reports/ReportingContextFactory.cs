using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Models.Reporting;

namespace RonSijm.RepoGen.ConfigurationExamples.Reports;

internal sealed class ReportingContextFactory(DbContextOptions<ReportingContext> options) : IDbContextFactory<ReportingContext>
{
    public List<ReportingContext> Contexts { get; } = [];
    public ReportingContext CreateDbContext() => throw new InvalidOperationException("This example expects the async factory path.");

    public Task<ReportingContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = new ReportingContext(options);
        Contexts.Add(context);
        return Task.FromResult(context);
    }
}