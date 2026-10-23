namespace RonSijm.RepoGen.Tool;

internal sealed record InMemoryWriteMappingModel(string WriteSource, string CopySource, string KeySource, string SameValuesSource,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<InMemoryPropertyModel>? MappedPropertyState = null);
