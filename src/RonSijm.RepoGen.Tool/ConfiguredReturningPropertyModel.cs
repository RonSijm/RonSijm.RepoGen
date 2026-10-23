namespace RonSijm.RepoGen.Tool;

internal sealed record ConfiguredReturningPropertyModel(string Name, string TypeName, bool IsShadow, bool IsNullable,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? AccessKind = null);
