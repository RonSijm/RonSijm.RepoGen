namespace RonSijm.RepoGen.Tool.Tests;

public sealed record NativeSavedView(int Id, string Text, int Version, string Parent, string Echo, IReadOnlyList<string> Siblings);
