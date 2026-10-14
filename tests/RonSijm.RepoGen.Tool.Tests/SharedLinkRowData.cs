namespace RonSijm.RepoGen.Tool.Tests;

public sealed record SharedLinkRowData(int Scope, int OwnerId, int TargetId, string Note, int Version, bool Hidden);
