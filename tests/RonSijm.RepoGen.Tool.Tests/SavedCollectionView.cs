namespace RonSijm.RepoGen.Tool.Tests;

public sealed record SavedCollectionView(int Id, string Name, int Version, string InputName, IReadOnlyList<string> Children);
