namespace RonSijm.RepoGen.Tool.Tests;

public sealed record SavedBatchRequest(string Label, IReadOnlyList<SavedBatchItem> Items);
