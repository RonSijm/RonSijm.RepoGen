namespace RonSijm.RepoGen.Tool.Tests;

public sealed record NavigationReadCard(int Id, string Name, string? Profile, int Count, int Total, IReadOnlyList<string> Authors);
