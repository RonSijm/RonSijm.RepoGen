namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ShadowReadCard(int Id, string Title, int Rank, string Code, int? Score, ShadowReadStatus Status, byte[]? Fingerprint);
