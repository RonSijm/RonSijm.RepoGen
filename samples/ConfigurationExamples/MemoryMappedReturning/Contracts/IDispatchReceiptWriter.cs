using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedReturning;

public interface IDispatchReceiptWriter
{
    Task<MutationResult<DispatchReceipt>> AdvanceAsync(ReceiptRequest request, CancellationToken cancellationToken = default);
    Task<MutationResult<DispatchReceipt>> ConsumeAsync(ReceiptRequest request, CancellationToken cancellationToken = default);
}
