namespace RonSijm.RepoGen.Tool;

/// <summary>Test-only scalar child-store lowering; never changes the production relationship model.</summary>
internal sealed record InMemoryCollectionCommandModel(string NavigationName, string RowTypeName, string StoreName,
    string StoreKeyTypeName, string BelongsToSource, string BindOwnerSource, string? CreateSource, InMemoryCommandModel State);
