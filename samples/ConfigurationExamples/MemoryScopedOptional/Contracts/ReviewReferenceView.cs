namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryScopedOptional;

public sealed record ReviewReferenceView(int ResourceId, string Caption, string? ReviewerRegion,
    string? ReviewerCode, string? ReviewerName, int Version);
