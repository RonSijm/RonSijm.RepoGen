namespace RonSijm.RepoGen.Tool.Tests;

public sealed record FilteredCollectionChildSeed(int Id, int ParentId, string Key, string Text, int Version = 1, bool Hidden = false);
