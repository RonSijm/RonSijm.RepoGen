namespace RonSijm.RepoGen.Tool;

internal sealed record ConfiguredAdditionalReferenceModel(string NavigationName, string TypeName,
    IReadOnlyList<string> ForeignKeys, IReadOnlyList<string> PrincipalKeys,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? Optional = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ConfiguredAdditionalSharedColumnModel>? SharedColumns = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? OptionalPresenceColumns = null);
