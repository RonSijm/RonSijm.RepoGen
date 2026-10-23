// Repository usage and executable assertions, not generator configuration.
// Start with Configuration/MembershipDirectoryRepositoryDesigner.cs and README.md.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.Membership;
using RonSijm.RepoGen.ConfigurationExamples.Models.Membership;

namespace RonSijm.RepoGen.ConfigurationExamples.Membership;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<MembershipDirectoryContext>(options => options.UseSqlite(connection));
        services.AddMembershipDirectoryContextRepositories();
        services.AddScoped<DirectoryService>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDirectoryContext>();
        await context.Database.EnsureCreatedAsync();
        context.Items.AddRange(new DirectoryItem { Id = 1, Name = "First", Category = "tools", Active = true },
            new DirectoryItem { Id = 2, Name = "Second", Category = "tools", Active = true },
            new DirectoryItem { Id = 3, Name = "Third", Category = "supplies", Active = true },
            new DirectoryItem { Id = 4, Name = "Hidden", Category = "tools", Active = false });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = scope.ServiceProvider.GetRequiredService<DirectoryService>();
        var ids = new[] { 2, 1, 2, 4 };
        if (!(await service.FindAsync(ids)).Select(item => item.Id).SequenceEqual([1, 2]) || !ids.SequenceEqual([2, 1, 2, 4]))
        {
            throw new InvalidOperationException("Membership normalization changed its input or bypassed visibility.");
        }

        if ((await service.FindAsync([])).Count != 0 || (await service.SearchAsync(new DirectorySearch(0, []))).Count != 0)
        {
            throw new InvalidOperationException("An empty membership input must not accidentally return all rows.");
        }

        if ((await service.SearchAsync(new DirectorySearch(0, null))).Count != 3 ||
            (await service.SearchAsync(new DirectorySearch(0, ["tools", null, "tools"]))).Count != 2)
        {
            throw new InvalidOperationException("The explicit optional/null-value policies were not applied.");
        }

        var counts = await service.CountsAsync(["tools"]);
        if (counts.Count != 1 || counts[0].Count != 2)
        {
            throw new InvalidOperationException("Grouped membership filtering failed.");
        }

        if (!(await service.SearchTextAsync(new DirectoryTextSearch(0, "tools"))).Select(item => item.Id).SequenceEqual([1, 2]) ||
            !(await service.SearchTextAsync(new DirectoryTextSearch(2, "Third"))).Select(item => item.Id).SequenceEqual([3]) ||
            (await service.SearchTextAsync(new DirectoryTextSearch(0, null))).Count != 3)
        {
            throw new InvalidOperationException("Composed optional text fragments did not preserve request binding or visibility.");
        }

        var minimumCounts = await service.CountsAtLeastAsync(new DirectoryCountSearch(2));
        if (minimumCounts.Count != 1 || minimumCounts[0].Category != "tools" || minimumCounts[0].Count != 2 ||
            (await service.CountsAtLeastAsync(new DirectoryCountSearch(0))).Count != 2)
        {
            throw new InvalidOperationException("Conditional DTO fragments did not filter grouped results correctly.");
        }

        var firstByCategory = await service.FirstByCategoryAsync();
        var lastByCategory = await service.LastByCategoryAsync();
        if (firstByCategory.Count != 2 || lastByCategory.Count != 2 || firstByCategory["tools"].Id != 1 || lastByCategory["tools"].Id != 2 ||
            firstByCategory["supplies"].Id != 3 || lastByCategory["supplies"].Id != 3)
        {
            throw new InvalidOperationException("Designer dictionary collision policies did not preserve ordering and visibility.");
        }

        try
        {
            await service.FindAsync(Enumerable.Repeat(1, 33).ToArray());
            throw new InvalidOperationException("The raw membership bound was not enforced.");
        }
        catch (ArgumentOutOfRangeException) { }

        if (context.ChangeTracker.Entries().Any())
        {
            throw new InvalidOperationException("Read-only ports must not track rows.");
        }

        Console.WriteLine("Membership: safe lists, composed fragments, optional text, grouped filters and first/last dictionary policies passed.");
    }
}

// Services know only the application port: no EF, Contains, input normalization or query-builder calls.
internal sealed class DirectoryService(IMembershipDirectory directory)
{
    public Task<IReadOnlyList<DirectoryItemSummary>> FindAsync(int[] ids, CancellationToken cancellationToken = default) => directory.FindByIdsAsync(ids, cancellationToken);
    public Task<IReadOnlyList<DirectoryItemSummary>> SearchAsync(DirectorySearch request, CancellationToken cancellationToken = default) => directory.SearchAsync(request, cancellationToken);
    public Task<IReadOnlyList<DirectoryCategoryCount>> CountsAsync(string[] categories, CancellationToken cancellationToken = default) => directory.CategoryCountsAsync(categories, cancellationToken);
    public Task<IReadOnlyList<DirectoryItemSummary>> SearchTextAsync(DirectoryTextSearch request, CancellationToken cancellationToken = default) => directory.SearchTextAsync(request, cancellationToken);
    public Task<IReadOnlyList<DirectoryCategoryCount>> CountsAtLeastAsync(DirectoryCountSearch request, CancellationToken cancellationToken = default) => directory.CategoryCountsAtLeastAsync(request, cancellationToken);
    public Task<IReadOnlyDictionary<string, DirectoryItemSummary>> FirstByCategoryAsync(CancellationToken cancellationToken = default) => directory.FirstByCategoryAsync(cancellationToken);
    public Task<IReadOnlyDictionary<string, DirectoryItemSummary>> LastByCategoryAsync(CancellationToken cancellationToken = default) => directory.LastByCategoryAsync(cancellationToken);
}
