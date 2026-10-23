namespace RonSijm.RepoGen.Tool;

/// <summary>Explicit test-only scalar assignments, never a persistence entity factory.</summary>
internal sealed record InMemoryEntityMappingModel(string ReadTypeName, string ExpressionSource, IReadOnlyList<InMemoryEntityMemberModel> Members,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<InMemoryEntityNavigationMemberModel>? Navigations = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<InMemoryPropertyModel>? PropertyMappings = null);
