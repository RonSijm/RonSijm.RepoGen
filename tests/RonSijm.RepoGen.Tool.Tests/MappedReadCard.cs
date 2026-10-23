namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedReadCard(int Id, string Name, string Caption, int Rank, int Audit, MappedReadState? State, byte[]? Fingerprint, string Tag, int FixedValue);
