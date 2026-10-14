namespace RonSijm.RepoGen.Tool;

internal sealed record InMemoryCommandModel(bool Supported, IReadOnlyList<string> Reasons,
    string SameValuesSource, string SameTokensSource, IReadOnlyList<string> MergeProperties,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? MappedCreateSource = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<InMemoryImmediateAssignmentModel>? ImmediateAssignments = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<InMemoryCollectionCommandModel>? Collections = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    InMemorySavedResultModel? SavedResult = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<InMemoryTranslationReceiptModel>? Translations = null,
    [property: System.Text.Json.Serialization.JsonIgnore]
    InMemoryCommandRenderingModel? Rendering = null,
    [property: System.Text.Json.Serialization.JsonIgnore]
    IReadOnlyList<InMemoryValueEqualityModel>? ValueEqualities = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    InMemoryCommandModelQueryFilterModel? ModelQueryFilter = null)
{
    internal bool IsImmediate => ImmediateAssignments is not null;
}
