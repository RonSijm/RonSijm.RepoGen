namespace RonSijm.RepoGen.Tool;

internal sealed record InMemoryQueryRootModel(string EntityTypeName, string VariableName, IReadOnlyList<string> VisibilityPredicates,
    string? StoreRowTypeName = null, InMemoryEntityMappingModel? EntityMapping = null);
