namespace RonSijm.RepoGen.Tool;

internal sealed record InMemoryStoreModel(string RowTypeName, string KeyTypeName, string KeySelectorSource, string CopySource,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    InMemoryEntityMappingModel? EntityMapping = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<InMemoryGeneratedValueModel>? GeneratedValues = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    InMemoryWriteMappingModel? WriteMapping = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? KeyComparerSource = null,
    [property: System.Text.Json.Serialization.JsonIgnore]
    IReadOnlyList<InMemoryValueEqualityModel>? ValueEqualities = null);
