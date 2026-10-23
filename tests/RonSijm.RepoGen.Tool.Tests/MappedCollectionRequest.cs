namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedCollectionRequest(int Id, string Title, IReadOnlyList<MappedCollectionItem> Items, int Revision = 3);
