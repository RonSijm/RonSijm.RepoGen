namespace RonSijm.RepoGen.Tool;

internal sealed record ConfiguredAdditionalReferenceModel(string NavigationName, string TypeName,
    IReadOnlyList<string> ForeignKeys, IReadOnlyList<string> PrincipalKeys,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? Optional = null);
