namespace RonSijm.RepoGen.Tool;

internal sealed record ConfiguredReturningStateModel(IReadOnlyList<ConfiguredReturningPropertyModel> Properties,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool IncludeMappedProperties = false);
