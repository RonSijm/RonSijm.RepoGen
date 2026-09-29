using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal sealed record RepositoryGenerationModel(
    string ContextTypeName,
    IReadOnlyList<EntityRepositoryModel> Entities,
    RepositoryGenerationSettingsModel Settings);

internal sealed record RepositoryGenerationSettingsModel(
    bool UseDbContextResolver,
    bool GenerateCacheDecorator,
    bool GenerateBatchOperations,
    bool GenerateSetBasedOperations,
    bool GenerateConcurrencyRetry,
    bool GenerateTransactions,
    bool GenerateSynchronousMethods,
    RepositoryTrackingBehavior DefaultTrackingBehavior);

internal sealed record EntityRepositoryModel(
    string Namespace,
    string Accessibility,
    string EntityName,
    string EntityTypeName,
    string ContextTypeName,
    bool GenerateSelectors,
    IReadOnlyList<LookupModel> Lookups,
    IReadOnlyList<FilterModel> Filters,
    IReadOnlyList<SortModel> DefaultSorts,
    IReadOnlyList<SortableFieldModel> SortableFields,
    IReadOnlyList<IncludeModel> Includes,
    IReadOnlyList<ProjectionModel> Projections,
    IReadOnlyList<ConfiguredQueryModel> Queries,
    IReadOnlyList<ConfiguredCommandModel> Commands,
    string CacheTag,
    IReadOnlyList<string> CacheInvalidationTags);

internal sealed record LookupModel(
    string MethodName,
    bool IsUnique,
    IReadOnlyList<LookupPropertyModel> Properties);

internal sealed record LookupPropertyModel(
    string PropertyName,
    string ParameterName,
    string TypeName);

internal sealed record FilterModel(
    string MethodName,
    bool IsUnique,
    IReadOnlyList<FilterParameterModel> Parameters,
    string PredicateSource);

internal sealed record FilterParameterModel(
    string Name,
    string TypeName);

internal sealed record SortModel(
    bool IsDescending,
    string ExpressionSource);

internal sealed record SortableFieldModel(string PropertyName);

internal sealed record IncludeModel(string ExpressionSource);

internal sealed record ProjectionModel(
    string Namespace,
    string Accessibility,
    string ProjectionTypeName,
    string SingleMethodName,
    string CollectionMethodName,
    string ExpressionSource,
    IReadOnlyList<IncludeModel> Includes);

internal sealed record ConfiguredQueryModel(
    string Namespace,
    string Accessibility,
    string MethodName,
    string ResultTypeName,
    RepositoryQueryResultKind ResultKind,
    IReadOnlyList<ConfiguredPredicateModel> Predicates,
    IReadOnlyList<FilterParameterModel> Parameters,
    IReadOnlyList<IncludeModel> Includes,
    IReadOnlyList<SortModel> Sorts,
    ConfiguredDynamicSortModel? DynamicSort,
    string ProjectionExpressionSource,
    string ProjectionBodySource,
    RepositoryTrackingBehavior TrackingBehavior,
    RepositoryQuerySplittingBehavior SplittingBehavior,
    bool IgnoreQueryFilters,
    string? QueryTag,
    ConfiguredQueryCacheModel Cache,
    ConfiguredCursorModel? Cursor,
    ConfiguredGroupedQueryModel? Group);

internal sealed record ConfiguredPredicateModel(string PredicateSource);

internal sealed record ConfiguredDynamicSortModel(
    string FieldNameParameter,
    string DescendingParameter,
    IReadOnlyList<SortableFieldModel> Fields);

internal sealed record ConfiguredQueryCacheModel(
    bool Enabled,
    long? AbsoluteExpirationTicks,
    long? SlidingExpirationTicks,
    IReadOnlyList<string> DependentEntityTypeNames);

internal sealed record ConfiguredCursorModel(
    string CursorTypeName,
    string SeekPredicateBodySource,
    string CursorSelectorExpressionSource,
    string CursorSelectorBodySource);

internal sealed record ConfiguredGroupedQueryModel(
    string KeySelectorExpressionSource,
    string ProjectionExpressionSource);

internal sealed record ConfiguredCommandModel(
    string MethodName,
    RepositoryCommandKind Kind,
    IReadOnlyList<ConfiguredPredicateModel> Predicates,
    IReadOnlyList<FilterParameterModel> Parameters,
    IReadOnlyList<ConfiguredUpdateModel> Updates);

internal sealed record ConfiguredUpdateModel(
    string PropertyExpressionSource,
    string ValueExpressionSource);