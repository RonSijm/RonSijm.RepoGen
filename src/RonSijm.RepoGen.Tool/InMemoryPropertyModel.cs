namespace RonSijm.RepoGen.Tool;

/// <summary>Explicit read-only scalar receipt; names are designer metadata, not runtime lookups.</summary>
internal sealed record InMemoryPropertyModel(string PropertyName, string MemberName, string TypeName, string ValueSource, string AccessKind);
