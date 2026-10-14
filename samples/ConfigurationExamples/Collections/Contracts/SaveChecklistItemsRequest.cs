namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.Collections;

public sealed record SaveChecklistItemsRequest(int ChecklistId, int OwnerId, int ExpectedVersion, IReadOnlyList<ChecklistItemInput> Items);
