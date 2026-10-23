using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal sealed record RepositoryGenerationModel(
    string ContextTypeName,
    IReadOnlyList<EntityRepositoryModel> Entities,
    RepositoryGenerationSettingsModel Settings,
    IReadOnlyList<RepositoryApiDataContract>? DataContracts = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<NavigationVisibilityOperationModel>? NavigationVisibility = null);

internal sealed record NavigationVisibilityOperationModel(
    string Entity,
    string Operation,
    string Kind,
    bool Required,
    IReadOnlyList<NavigationVisibilityAccessModel> Accesses);

internal sealed record NavigationVisibilityAccessModel(
    string DeclaringEntity,
    string Navigation,
    string TargetEntity,
    string Policy,
    string? ReviewReason,
    string? ReviewScope);

internal sealed record RepositoryGenerationSettingsModel(
    bool UseDbContextResolver,
    bool GenerateCacheDecorator,
    bool GenerateBatchOperations,
    bool GenerateSetBasedOperations,
    bool GenerateConcurrencyRetry,
    bool GenerateTransactions,
    bool GenerateSynchronousMethods,
    RepositoryTrackingBehavior DefaultTrackingBehavior,
    string? ContractNamespace = null,
    bool UseAsyncContextLeases = false,
    RepositoryTrackingBehavior ReadOnlyTrackingBehavior = RepositoryTrackingBehavior.NoTracking,
    bool GenerateTrackingMethods = true,
    string? NoTrackingMethodName = "AsNoTracking",
    string? TrackingMethodName = "AsTracking",
    string? NoTrackingWithIdentityResolutionMethodName = "AsNoTrackingWithIdentityResolution",
    bool ClosedCaching = false,
    string? SingleContextCacheScope = null,
    string? ContextKeyTypeName = null,
    string? AtomicWriterTypeName = null,
    int? CommandTimeoutSeconds = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? ReturningWriterTypeName = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool CqrsSelectors = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? GeneratedNamespace = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool RequireNavigationVisibilityReview = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? ScriptedTestDoubleNamespace = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool InMemoryTestStores = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool InMemoryTestQueries = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool RequirePortableTestQueries = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool InMemoryTestCommands = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool RequirePortableTestCommands = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string InfrastructureNamespace => GeneratedNamespace ?? "RonSijm.RepoGen";

    [System.Text.Json.Serialization.JsonIgnore]
    public string InfrastructurePrefix => "global::" + InfrastructureNamespace + ".";
}

internal sealed record EntityRepositoryModel(
    string Namespace,
    string Accessibility,
    string EntityName,
    string EntityTypeName,
    string ContextTypeName,
    bool GenerateSelectors,
    bool ProjectionEnabled,
    IReadOnlyList<LookupModel> Lookups,
    IReadOnlyList<FilterModel> Filters,
    IReadOnlyList<SortModel> DefaultSorts,
    IReadOnlyList<SortableFieldModel> SortableFields,
    IReadOnlyList<IncludeModel> Includes,
    IReadOnlyList<ProjectionModel> Projections,
    IReadOnlyList<ConfiguredQueryModel> Queries,
    IReadOnlyList<ConfiguredCommandModel> Commands,
    string CacheTag,
    IReadOnlyList<string> CacheInvalidationTags,
    bool ReadEnabled = true,
    bool WriteEnabled = true,
    IReadOnlyList<BoundPortModel>? ReadPorts = null,
    IReadOnlyList<BoundPortModel>? WritePorts = null,
    IReadOnlyList<NamedLoadProfileModel>? LoadProfiles = null,
    IReadOnlyList<VisibilityPolicyModel>? VisibilityPolicies = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ConfiguredCommandTimeoutModel? CommandTimeout = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? ProtectedWriteProperties = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool GenerateTestDouble = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    InMemoryStoreModel? InMemoryStore = null);

internal sealed record VisibilityPolicyModel(string Name, string PredicateSource);

internal sealed record BoundPortModel(
    string TypeName,
    IReadOnlyList<string> OperationSignatures,
    IReadOnlyList<RepositoryApiMethod>? Methods = null,
    IReadOnlyList<string>? InheritedTypes = null,
    [property: System.Text.Json.Serialization.JsonIgnore]
    IReadOnlyList<Type>? ApiDataTypes = null);

internal sealed record LookupModel(
    string MethodName,
    bool IsUnique,
    IReadOnlyList<LookupPropertyModel> Properties);

internal sealed record LookupPropertyModel(
    string PropertyName,
    string ParameterName,
    string TypeName,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? ComparisonSource = null,
    [property: System.Text.Json.Serialization.JsonIgnore]
    Type? RuntimeType = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    MappedPropertyBindingModel? MappedProperty = null);

internal sealed record FilterModel(
    string MethodName,
    bool IsUnique,
    IReadOnlyList<FilterParameterModel> Parameters,
    string PredicateSource);

internal sealed record FilterParameterModel(
    string Name,
    string TypeName,
    bool NullableReference = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? ClrTypeName = null,
    [property: System.Text.Json.Serialization.JsonIgnore]
    Type? RuntimeType = null);

internal sealed record SortModel(
    bool IsDescending,
    string ExpressionSource);

internal sealed record SortableFieldModel(string PropertyName);

internal sealed record IncludeModel(
    string ExpressionSource,
    IReadOnlyList<string>? ThenIncludes = null,
    IReadOnlyList<IncludePathModel>? Paths = null,
    IReadOnlyList<string>? DependentEntityTypeNames = null);

internal sealed record IncludePathModel(string Path, bool IsCollection, string? FilterSource);

internal sealed record NamedLoadProfileModel(string Name, IReadOnlyList<IncludeModel> Includes);

internal sealed record ProjectionModel(
    string Namespace,
    string Accessibility,
    string ProjectionTypeName,
    string SingleMethodName,
    string CollectionMethodName,
    string ExpressionSource,
    IReadOnlyList<IncludeModel> Includes,
    string? NullableExpressionSource = null);

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
    ConfiguredGroupedQueryModel? Group,
    ConfiguredResultPipelineModel? Pipeline = null,
    ConfiguredDictionaryModel? Dictionary = null,
    ConfiguredScalarModel? Scalar = null,
    bool UseDefaultSort = true,
    ConfiguredQuerySourceModel? Source = null,
    IReadOnlyList<string>? LoadProfileNames = null,
    IReadOnlyList<string>? BypassedVisibility = null,
    string? ContextKeySource = null,
    IReadOnlyList<ConfiguredInputValidationModel>? Validations = null,
    ConfiguredRegisteredQueryModel? RegisteredRoot = null,
    RepositoryPageConsistency PageConsistency = RepositoryPageConsistency.BestEffort,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool ResultNullabilityConfigured = false,
    [property: System.Text.Json.Serialization.JsonIgnore]
    IReadOnlyList<Type>? ApiDataTypes = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? GeneratedNamespace = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ConfiguredMembershipModel>? Memberships = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    RepositoryQueryTarget Target = RepositoryQueryTarget.Read,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ConfiguredCommandTimeoutModel? CommandTimeout = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ConfiguredInputValidationModel>? CursorValidations = null,
    [property: System.Text.Json.Serialization.JsonIgnore]
    InMemoryQueryModel? InMemoryQuery = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool IgnoreAutoIncludes = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string InfrastructurePrefix => "global::" + (GeneratedNamespace ?? "RonSijm.RepoGen") + ".";
}

internal sealed record ConfiguredRegisteredQueryModel(string ProviderTypeName, string FieldName);

internal sealed record ConfiguredCommandTimeoutModel(int? Seconds, string Source);

internal sealed record ConfiguredInputValidationModel(string PredicateSource, string MessageSource);

internal sealed record ConfiguredMembershipModel(
    string LocalName,
    string ElementTypeName,
    string ParameterName,
    string ValuesSource,
    string PredicateSource,
    bool Negated,
    RepositoryMembershipOptions Options)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string ValuesName => LocalName + "Values";

    [System.Text.Json.Serialization.JsonIgnore]
    public string ApplyName => LocalName + "Apply";
}

internal sealed record ConfiguredQuerySourceModel(
    string ExpressionSource,
    IReadOnlyList<string> EntityTypeNames,
    IReadOnlyList<string> Operators);

internal sealed record ConfiguredResultPipelineModel(
    IReadOnlyList<ConfiguredQueryStageModel> Stages,
    bool ResultIsNonNullableValueType);

internal sealed record ConfiguredQueryStageModel(
    RepositoryQueryStageKind Kind,
    string? ExpressionSource,
    string? ConditionSource,
    string InputTypeName,
    string? ProjectionExpressionSource = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ConfiguredFieldSortModel? FieldSort = null);

internal sealed record ConfiguredFieldSortModel(
    string LocalName,
    string ParameterName,
    string FieldNameSource,
    string DescendingSource,
    IReadOnlyList<ConfiguredSortFieldModel> Fields,
    bool PreserveOrderWhenUnspecified,
    string? DefaultField,
    bool DefaultDescending)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string IndexName => LocalName + "Index";

    [System.Text.Json.Serialization.JsonIgnore]
    public string DescendingName => LocalName + "Descending";
}

internal sealed record ConfiguredSortFieldModel(string Name, string KeySelectorExpressionSource);

internal sealed record ConfiguredDictionaryModel(
    string KeyTypeName,
    string ValueTypeName,
    string KeyBodySource,
    string ValueBodySource,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    RepositoryDuplicateKeyBehavior DuplicateKeys = RepositoryDuplicateKeyBehavior.Throw);

internal sealed record ConfiguredScalarModel(string ReturnTypeName, string SelectorExpressionSource);

internal sealed record ConfiguredPredicateModel(string PredicateSource, string? ConditionSource = null);

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
    string CursorSelectorBodySource,
    bool OnResults = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? RequestParameterName = null);

internal sealed record ConfiguredGroupedQueryModel(
    string KeySelectorExpressionSource,
    string ProjectionExpressionSource);

internal sealed record ConfiguredCommandModel(
    string MethodName,
    RepositoryCommandKind Kind,
    IReadOnlyList<ConfiguredPredicateModel> Predicates,
    IReadOnlyList<FilterParameterModel> Parameters,
    IReadOnlyList<ConfiguredUpdateModel> Updates,
    string? CreateSource = null,
    string? ResultTypeName = null,
    string? ResultSource = null,
    bool SaveChanges = true,
    bool ReloadAfterSave = false,
    IReadOnlyList<ConfiguredVersionModel>? ExpectedVersions = null,
    bool WithOutcome = false,
    IReadOnlyList<string>? BypassedVisibility = null,
    string? ContextKeySource = null,
    IReadOnlyList<ConfiguredInputValidationModel>? Validations = null,
    IReadOnlyList<string>? InsertColumns = null,
    IReadOnlyList<string>? ConflictColumns = null,
    [property: System.Text.Json.Serialization.JsonIgnore]
    IReadOnlyList<Type>? ApiDataTypes = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool IsReturning = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? ReturningKeys = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ConfiguredCommandTimeoutModel? CommandTimeout = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ConfiguredCollectionReconciliationModel>? Collections = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? AllowedProtectedWrites = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ConfiguredBatchCommandModel? Batch = null,
    [property: System.Text.Json.Serialization.JsonIgnore]
    InMemoryCommandModel? InMemoryCommand = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? SavedResultKeys = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ConfiguredCreateManyModel? CreateMany = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ConfiguredReturningStateModel? ReturningState = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? SavedResultMaximumRows = null)
{
    public bool IsAtomic => Kind is RepositoryCommandKind.InsertIfAbsent or RepositoryCommandKind.InsertOrUpdate;
    public bool IsTracked => Kind is RepositoryCommandKind.Create or RepositoryCommandKind.Patch or RepositoryCommandKind.TrackedDelete;

    [System.Text.Json.Serialization.JsonIgnore]
    public string? ResultValueTypeName => ResultTypeName is null ? null : Batch is null && CreateMany is null ? ResultTypeName : "global::System.Collections.Generic.IReadOnlyList<" + ResultTypeName + ">";

    public string ReturnTypeName => IsReturning ? "global::System.Threading.Tasks.Task<global::RonSijm.RepoGen.Abstractions.MutationResult<" + ResultTypeName + ">>"
        : !IsTracked && !IsAtomic && !WithOutcome ? "global::System.Threading.Tasks.Task<int>"
        : "global::System.Threading.Tasks.Task<global::RonSijm.RepoGen.Abstractions.CommandResult" + (ResultValueTypeName is null ? "" : "<" + ResultValueTypeName + ">") + ">";
}

internal sealed record ConfiguredVersionModel(string PropertyName, string ValueSource,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    MappedPropertyBindingModel? MappedProperty = null);

internal sealed record ConfiguredUpdateModel(
    string PropertyExpressionSource,
    string ValueExpressionSource,
    string? PropertyName = null,
    string? ValueBodySource = null,
    string? ConditionSource = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? NativeValueTypeName = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? NativeValueAlias = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    MappedPropertyBindingModel? MappedProperty = null);
