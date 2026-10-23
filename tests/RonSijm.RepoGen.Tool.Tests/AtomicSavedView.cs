namespace RonSijm.RepoGen.Tool.Tests;

public sealed record AtomicSavedView(int Id, string Text, int Version, string ParentName, string Echo, IReadOnlyList<string> Siblings);
