namespace RonSijm.RepoGen.Tool;

internal sealed record InMemoryGeneratedValueModel(string PropertyName, string ValueSource, string CreateCondition,
    bool OnCreate, bool OnUpdate,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? ModelDefaultSource = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? CustomSentinelSource = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? StateMemberName = null);
