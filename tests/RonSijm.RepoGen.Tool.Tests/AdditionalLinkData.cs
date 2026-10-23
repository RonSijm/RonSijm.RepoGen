namespace RonSijm.RepoGen.Tool.Tests;

public sealed record AdditionalLinkData(int Scope, int OwnerId, int TargetId, string ActorRegion, string ActorCode,
    int SourceId, string Note, int Version, bool Hidden);
