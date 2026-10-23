namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryScopedOptional;

public sealed record ReviewReferenceInput(int PartitionId, int ResourceId, string? ReviewerRegion,
    string? ReviewerCode, string Caption, int ExpectedVersion = 1);
