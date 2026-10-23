namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedKeyLinkSeed(int Id, int? OwnerScope, string? OwnerCode, int? TagId, bool Removed = false);
