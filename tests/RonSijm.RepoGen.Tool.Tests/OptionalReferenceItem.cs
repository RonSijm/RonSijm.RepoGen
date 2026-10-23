namespace RonSijm.RepoGen.Tool.Tests;

public sealed record OptionalReferenceItem(int TargetId, int? ReviewerRegion, string? ReviewerCode, int? SourceId, string Note, int ExpectedVersion = 1);

