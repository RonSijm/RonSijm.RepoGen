namespace RonSijm.RepoGen.Tool;

internal sealed record InMemoryManyToManyJoinModel(string EntityTypeName, string VariableName,
    string SourceKeySource, string TargetKeySource, bool RequiredSource, bool RequiredTarget,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool PreserveMissingTargets = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? SourceKeyComparerSource = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? TargetKeyComparerSource = null);
