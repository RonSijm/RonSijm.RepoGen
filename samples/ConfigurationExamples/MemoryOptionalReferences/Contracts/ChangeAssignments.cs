namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryOptionalReferences;

public sealed record ChangeAssignments(int WorkspaceId, int ExpectedVersion, IReadOnlyList<AssignmentInput> Items);

