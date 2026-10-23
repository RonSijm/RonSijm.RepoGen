namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryOptionalReferences;

public sealed record WorkspaceView(int Id, int Version, IReadOnlyList<AssignmentView> Items);

