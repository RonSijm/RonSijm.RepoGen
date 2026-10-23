namespace RonSijm.RepoGen.Tool;

internal sealed record InMemorySavedResultModel(
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    InMemoryNavigationModel? Navigation,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<InMemoryModelQueryFilterModel>? ModelQueryFilters = null);
