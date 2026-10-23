namespace RonSijm.RepoGen.Tool.Tests;

public sealed record OverlappingReferenceItem(int Scope, int TargetId, int Zone, string ActorCode,
    string ReviewerCode, int SourceId, string Note, int ExpectedVersion = 1);
