// Paired with ../Model/InventoryContext.cs by ../../GenerationRunner/Program.cs.
// API: ../../Contracts/Generated/InventoryRepositories.g.cs; implementation: ../Generated/Repositories.g.cs.
// See ../README.md for the declaration-to-output map; the consumer does not run this designer.
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.Inventory;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class InventoryRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        // Direct context, no cache/resolver/retry/lease boilerplate; only native returning writes are selected.
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.Inventory")
            .GenerateOnlyConfiguredEntities().UseReturningWrites<SqliteReturningWriter>().IncludeApiDataContracts());
        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.BindWritePort<IInventoryWrites>();
            entity.HasQuery("ListStock").OrderBy(item => item.Id)
                .Select(item => new InventoryChange(item.Id, item.Sku, item.Quantity, item.Version)).List();
            entity.HasCommand("AdjustStock")
                .Validate<AdjustStockRequest>(request => request.Delta != 0, "A stock adjustment must be nonzero.")
                .Where<AdjustStockRequest>((item, request) => item.Category == request.Category)
                .Update(setters => setters
                    .SetProperty<int, AdjustStockRequest>(item => item.Quantity, (item, request) => item.Quantity + request.Delta)
                    .SetProperty(item => item.Version, item => item.Version + 1))
                .AllowWrite(item => item.Version)
                .ReturnsMany(item => new InventoryChange(item.Id, item.Sku, item.Quantity, item.Version));
            entity.HasCommand("RemoveEmpty").Where(item => item.Quantity == 0).Delete()
                .ReturnsMany(item => new InventoryChange(item.Id, item.Sku, item.Quantity, item.Version));
        });
    }
}
