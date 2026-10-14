// This example's application contracts; compiled by the shared EF-free Contracts project.
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public sealed record AdjustStockRequest(string Category, int Delta);

public sealed record InventoryChange(int Id, string Sku, int Quantity, int Version);

public interface IInventoryWrites
{
    Task<MutationResult<InventoryChange>> AdjustStockAsync(AdjustStockRequest request, CancellationToken cancellationToken = default);
    Task<MutationResult<InventoryChange>> RemoveEmptyAsync(CancellationToken cancellationToken = default);
}
