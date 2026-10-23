// Repository usage and executable assertions, not generator configuration.
// Start with Configuration/InventoryRepositoryDesigner.cs and README.md.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.Inventory;
using RonSijm.RepoGen.ConfigurationExamples.Models.Inventory;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Inventory;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<InventoryContext>(options => options.UseSqlite(connection));
        services.AddSingleton<SqliteReturningWriter>();
        services.AddInventoryContextRepositories();
        services.AddScoped<InventoryService>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryContext>();
        await context.Database.EnsureCreatedAsync();
        context.Items.AddRange(new InventoryItem { Sku = "A", Category = "clearance", Quantity = 1 },
            new InventoryItem { Sku = "B", Category = "clearance", Quantity = 1 }, new InventoryItem { Sku = "C", Category = "regular", Quantity = 10 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = scope.ServiceProvider.GetRequiredService<InventoryService>();
        var adjusted = await service.AdjustAsync(new AdjustStockRequest("clearance", -1));
        if (adjusted.AffectedRows != 2 || adjusted.Items.Any(item => item.Quantity != 0 || item.Version != 2))
        {
            throw new InvalidOperationException("Native stock adjustment did not return every updated row.");
        }

        var removed = await service.RemoveEmptyAsync();
        if (removed.AffectedRows != 2 || !removed.Items.OrderBy(item => item.Id).SequenceEqual(adjusted.Items.OrderBy(item => item.Id)))
        {
            throw new InvalidOperationException("Native deletion did not return the deleted values.");
        }

        var remaining = await scope.ServiceProvider.GetRequiredService<IReadInventoryItemRepository>().ListStockAsync();
        if (remaining.Count != 1 || remaining[0].Sku != "C" || context.ChangeTracker.Entries().Any())
        {
            throw new InvalidOperationException("Returning writes unexpectedly tracked rows or modified another category.");
        }

        Console.WriteLine("Inventory: two native updates, two native deletes, detached results and simple direct-context DI passed.");
    }
}

// Application code sees just its port, request, and result: no EF, setter lambdas, tracking or SaveChanges calls.
internal sealed class InventoryService(IInventoryWrites writes)
{
    public Task<MutationResult<InventoryChange>> AdjustAsync(AdjustStockRequest request, CancellationToken cancellationToken = default)
        => writes.AdjustStockAsync(request, cancellationToken);

    public Task<MutationResult<InventoryChange>> RemoveEmptyAsync(CancellationToken cancellationToken = default)
        => writes.RemoveEmptyAsync(cancellationToken);
}
