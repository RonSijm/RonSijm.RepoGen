namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedCollectionView(int Id, int Revision, IReadOnlyList<MappedCollectionCard> Items);
