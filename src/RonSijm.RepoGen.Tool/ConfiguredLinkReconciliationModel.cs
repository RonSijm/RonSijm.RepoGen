namespace RonSijm.RepoGen.Tool;

internal sealed record ConfiguredLinkReconciliationModel(string TargetTypeName,
    IReadOnlyList<string> OwnerForeignKeys, IReadOnlyList<string> TargetForeignKeys,
    IReadOnlyList<string> TargetPrincipalKeys,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ConfiguredSharedReferenceColumnModel>? SharedReferenceColumns = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? TargetNavigationName = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ConfiguredAdditionalReferenceModel>? AdditionalReferences = null);
