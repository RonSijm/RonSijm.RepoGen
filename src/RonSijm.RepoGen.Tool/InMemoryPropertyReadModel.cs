namespace RonSijm.RepoGen.Tool;

/// <summary>One actually used typed model-property read, scoped to its source dataset.</summary>
internal sealed record InMemoryPropertyReadModel(string EntityTypeName, string PropertyName, string TypeName, string AccessKind);
