namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.Collections;

public sealed record ChecklistView(int Id, int Version, IReadOnlyList<ChecklistItemInput> Items);
