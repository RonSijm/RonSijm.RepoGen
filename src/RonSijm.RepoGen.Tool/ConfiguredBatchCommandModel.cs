namespace RonSijm.RepoGen.Tool;

internal sealed record ConfiguredBatchCommandModel(
    string ItemTypeName,
    string KeyTypeName,
    string ItemsSource,
    string EntityKeySource,
    string ItemKeySource,
    string KeyPropertyName,
    int MaximumItems,
    IReadOnlyList<ConfiguredUpdateModel> Updates,
    IReadOnlyList<ConfiguredVersionModel> ExpectedVersions);
