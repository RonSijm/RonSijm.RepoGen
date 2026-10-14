namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryProxyWrites;

// Different names from the persistence model, with extra arrangement-only metadata.
public sealed record NoteSeed(int Key, string Caption, int Revision, bool Archived, byte[] Marker, string Tag = "arranged");
