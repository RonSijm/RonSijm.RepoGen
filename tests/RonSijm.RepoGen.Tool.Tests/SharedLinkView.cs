namespace RonSijm.RepoGen.Tool.Tests;

public sealed record SharedLinkView(int SpaceId, int Id, int Version, IReadOnlyList<string> Notes);
