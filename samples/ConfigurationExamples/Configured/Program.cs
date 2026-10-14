// Repository usage and executable assertions, not generator configuration.
// Start with Configuration/CatalogRepositoryDesigner.cs and README.md.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.Catalog;

namespace RonSijm.RepoGen.ConfigurationExamples.Configured;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await using var context = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>().UseSqlite("Data Source=:memory:").Options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new SampleMemoryCacheProvider(memoryCache);
        var repository = new ProductRepositoryCacheDecorator(new ProductRepository(context), cache);
        repository.AddRange([
            new Product { Sku = "TEA", Name = "Tea", Category = "drinks", Amount = 3 },
            new Product { Sku = "COFFEE", Name = "Coffee", Category = "drinks", Amount = 4 },
            new Product { Sku = "BREAD", Name = "Bread", Category = "food", Amount = 5 }
        ]);
        await repository.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Optional legacy selector reads use the designer's neutral name, not EF terminology.
        var tea = await repository.BySku("TEA").AsReadOnly().AsEntityAsync();
        if (tea?.Name != "Tea" || context.ChangeTracker.Entries().Any())
        {
            throw new InvalidOperationException("The neutral read did not disable tracking.");
        }

        // One named method; filtering, safe sort fields, projection, cache and paging were designed already.
        var page = await repository.GetProductsAsync("drinks", "Name", false, 1, 10);
        if (page.TotalCount != 2 || page.Items[0].Name != "Coffee")
        {
            throw new InvalidOperationException("Unexpected catalog page.");
        }

        await repository.GetProductsAsync("drinks", "Name", false, 1, 10);
        if (cache.FactoryCalls != 1)
        {
            throw new InvalidOperationException("The second named read did not use the cache.");
        }

        repository.Add(new Product { Sku = "WATER", Name = "Water", Category = "drinks", Amount = 1 });
        await repository.SaveChangesAsync();
        page = await repository.GetProductsAsync("drinks", "Name", false, 1, 10);
        if (page.TotalCount != 3 || cache.FactoryCalls != 2)
        {
            throw new InvalidOperationException("A saved write did not invalidate the cache.");
        }

        Console.WriteLine("Configured: neutral read-only selector, reviewed DTO, whitelisted sort, batch staging, named page and cache invalidation passed.");
    }
}
