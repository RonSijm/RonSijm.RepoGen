namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNativeResults;

public interface ITicketQueueReader
{
    Task<string> NameAsync(int queueId, CancellationToken cancellationToken = default);
}
