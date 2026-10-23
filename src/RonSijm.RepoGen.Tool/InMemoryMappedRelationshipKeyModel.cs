namespace RonSijm.RepoGen.Tool;

/// <summary>A named read binding consumed by one relationship role, not a fixture-store or tracked-state key.</summary>
internal sealed record InMemoryMappedRelationshipKeyModel(string Role, string EntityTypeName, string PropertyName,
    string MemberName, string TypeName, string AccessKind);
