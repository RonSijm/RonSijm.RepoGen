namespace RonSijm.RepoGen.Tool;

internal sealed record InMemoryNavigationEdgeModel(string Name, string SourceVariableName, string TargetVariableName,
    bool IsCollection, bool Required, string SourceKeySource, string TargetKeySource, string AssignmentSource,
    InMemoryManyToManyJoinModel? Join = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? KeyComparerSource = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<InMemoryMappedRelationshipKeyModel>? MappedPropertyKeys = null);
