namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNativeResults;

public sealed record QueueTicketReceipt(int Id, string Label, int Revision, string Queue, IReadOnlyList<string> Labels);
