namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryBatches;

public sealed record BatchDocumentsRequest(int OwnerId, IReadOnlyList<BatchDocumentChange> Items);
