namespace RonSijm.RepoGen.Tool;

internal sealed record InMemoryTranslationReceiptModel(string Name, string Method,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? ResultPolicy = null);
