using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal sealed record ConfiguredCollectionReconciliationModel(
    string NavigationName, string NavigationSource, string ChildTypeName, string ItemTypeName, string KeyTypeName,
    string ItemsSource, string ChildKeySource, string ItemKeySource, string? ConditionSource,
    string? CreateSource, IReadOnlyList<ConfiguredUpdateModel> Updates, IReadOnlyList<ConfiguredVersionModel> ExpectedVersions,
    int MaximumItems, RepositoryCollectionRemovalBehavior MissingItems, string ChildCacheTag,
    IReadOnlyList<string> VisibilityPredicates,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? ProtectedWriteProperties = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? AllowedProtectedWrites = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? MatchingKeyProperties = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ConfiguredLinkReconciliationModel? Link = null);
