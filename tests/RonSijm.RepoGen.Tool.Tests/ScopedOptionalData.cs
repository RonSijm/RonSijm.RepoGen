namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ScopedOptionalData(int Scope, int Zone, int OwnerId, int TargetId, string ActorRegion,
    string ActorCode, string? ReviewerRegion, string? ReviewerCode, int? OriginId, string Note, int Version = 1, bool Hidden = false);
