namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedCollections;

public sealed record VersionedChecklistChange(int Id, string Title, int ExpectedRevision, IReadOnlyList<VersionedItemChange> Items);
