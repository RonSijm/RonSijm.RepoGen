namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedReadSeed(int Id, string Name, string Caption, int Rank, int Audit, bool Archived,
    MappedReadState? State, byte[]? Fingerprint, string Tag, int? FolderId, int FixedValue = 17);
