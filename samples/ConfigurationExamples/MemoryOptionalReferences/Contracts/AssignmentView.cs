namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryOptionalReferences;

public sealed record AssignmentView(int SubjectId, int? OriginPartition, string? OriginCode, string Caption, int Version);

