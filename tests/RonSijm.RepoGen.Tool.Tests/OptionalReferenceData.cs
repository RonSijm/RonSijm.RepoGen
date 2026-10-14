namespace RonSijm.RepoGen.Tool.Tests;

public sealed record OptionalReferenceData(int OwnerId, int TargetId, int? ReviewerRegion, string? ReviewerCode, int? SourceId, string Note, int Version, bool Hidden);

