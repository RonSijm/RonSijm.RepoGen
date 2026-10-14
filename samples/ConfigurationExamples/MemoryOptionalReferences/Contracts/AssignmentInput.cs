namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryOptionalReferences;

public sealed record AssignmentInput(int SubjectId, int? OriginPartition, string? OriginCode, string Caption, int ExpectedVersion = 1);

