namespace RonSijm.RepoGen.Tool.Tests;

public sealed record NullTargetReadCard(int Id, string Name, int Total, int Missing, IReadOnlyList<string?> Tags);
