namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedReturning;

public interface IDispatchReceiptReader
{
    Task<IReadOnlyList<DispatchReceipt>> ListAsync(CancellationToken cancellationToken = default);
}
