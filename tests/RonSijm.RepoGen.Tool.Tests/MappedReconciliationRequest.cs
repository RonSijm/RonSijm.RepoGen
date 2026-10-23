namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedReconciliationRequest(int Id, string Title, IReadOnlyList<MappedCollectionItem> Items,
    int Revision = 3, bool IncludeChildren = true);
