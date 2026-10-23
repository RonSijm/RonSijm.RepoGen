namespace RonSijm.RepoGen.Tool;

internal sealed record InMemoryNavigationRootModel(string EntityTypeName, string VariableName,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? StoreRowTypeName = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    InMemoryEntityMappingModel? EntityMapping = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    InMemoryModelQueryFilterModel? ModelQueryFilter = null);
