using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNativeResults;

public interface IQueueTicketWriter
{
    Task<MutationResult<QueueTicketReceipt>> RelabelAsync(QueueRelabelRequest request, CancellationToken cancellationToken = default);
    Task<MutationResult<QueueTicketReceipt>> RelabelAtMostOneAsync(QueueRelabelRequest request, CancellationToken cancellationToken = default);
}
