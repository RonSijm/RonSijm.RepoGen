namespace RonSijm.RepoGen.Tool.Tests;

public sealed record OverlappingReferenceLinkData(int Scope, int Zone, int OwnerId, int TargetId,
    string ActorCode, string ReviewerCode, int SourceId, string Note, int Version = 1, bool Hidden = false);
