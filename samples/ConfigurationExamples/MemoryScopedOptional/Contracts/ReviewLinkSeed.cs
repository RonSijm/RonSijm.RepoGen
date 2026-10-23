namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryScopedOptional;

public sealed record ReviewLinkSeed(int PartitionId, int FolderId, int ResourceId, string? ReviewerRegion,
    string? ReviewerCode, string Caption, int Version = 1);
