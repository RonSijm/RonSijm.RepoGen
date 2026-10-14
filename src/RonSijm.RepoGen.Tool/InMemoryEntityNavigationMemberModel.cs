namespace RonSijm.RepoGen.Tool;

/// <summary>A declared relationship slot on a plain mapped read row, not an EF navigation instance.</summary>
internal sealed record InMemoryEntityNavigationMemberModel(string Name, string TargetTypeName, bool IsCollection, bool Required);
