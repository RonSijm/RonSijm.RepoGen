namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryProxyWrites;

public sealed record NoteChange(int Id, string Title, int ExpectedRevision = 3, byte[]? Marker = null);
