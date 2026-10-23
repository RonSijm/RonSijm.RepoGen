namespace RonSijm.RepoGen.Tool.Tests;

public sealed record AdditionalLinkView(int Scope, int Id, int Version, IReadOnlyList<string> References);
