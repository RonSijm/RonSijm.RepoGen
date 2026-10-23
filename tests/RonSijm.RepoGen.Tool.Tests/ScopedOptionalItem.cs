namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ScopedOptionalItem(int Scope, int TargetId, int Zone, string? ReviewerRegion,
    string? ReviewerCode, int? OriginId, string Note, int ExpectedVersion = 1);
