namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryOptionalReferences;

public sealed record AssignmentSeed(int WorkspaceId, int SubjectId, int? OriginPartition, string? OriginCode, string Caption, int Version);

