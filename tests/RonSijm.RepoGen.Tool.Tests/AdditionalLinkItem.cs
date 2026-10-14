namespace RonSijm.RepoGen.Tool.Tests;

public sealed record AdditionalLinkItem(int Scope, int TargetId, string ActorRegion, string ActorCode,
    int SourceId, string Note, int ExpectedVersion = 1);
