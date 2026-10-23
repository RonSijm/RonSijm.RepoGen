namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ShadowReadSeed(int Id, string Title, bool Hidden, int? FolderId, int Rank, string Code,
    int? Score, bool Deleted, ShadowReadStatus Status, byte[]? Fingerprint);
