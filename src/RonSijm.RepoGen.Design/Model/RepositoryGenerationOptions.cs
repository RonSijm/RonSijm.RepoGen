namespace RonSijm.RepoGen.Design;

public sealed class RepositoryGenerationOptions
{
    public string? ScriptedTestDoubleNamespace { get; internal set; }
    public bool InMemoryTestStores { get; internal set; }
    public bool InMemoryTestQueries { get; internal set; }
    public bool RequirePortableTestQueries { get; internal set; }
    public bool InMemoryTestCommands { get; internal set; }
    public bool RequirePortableTestCommands { get; internal set; }
    public bool RequireNavigationVisibilityReview { get; internal set; }
    public string? GeneratedNamespace { get; internal set; }
    public bool CqrsSelectors { get; internal set; }
    public bool IncludeApiDataContracts { get; internal set; }
    public bool ClosedCaching { get; internal set; }
    public string? SingleContextCacheScope { get; internal set; }
    public Type? ContextKeyType { get; internal set; }
    public Type? AtomicWriterType { get; internal set; }
    public Type? ReturningWriterType { get; internal set; }
    public int? CommandTimeoutSeconds { get; internal set; }
    public string? ContractNamespace { get; internal set; }

    public bool UseAsyncContextLeases { get; internal set; }

    public bool GenerateOnlyConfiguredEntities { get; internal set; }

    public bool UseDbContextResolver { get; internal set; }

    public bool GenerateCacheDecorator { get; internal set; } = true;

    public bool GenerateBatchOperations { get; internal set; } = true;

    public bool GenerateSetBasedOperations { get; internal set; }

    public bool GenerateConcurrencyRetry { get; internal set; } = true;

    public bool GenerateTransactions { get; internal set; } = true;

    public bool GenerateSynchronousMethods { get; internal set; } = true;

    public RepositoryTrackingBehavior DefaultTrackingBehavior { get; internal set; }

    public RepositoryTrackingBehavior ReadOnlyTrackingBehavior { get; internal set; } = RepositoryTrackingBehavior.NoTracking;

    public bool GenerateTrackingMethods { get; internal set; } = true;

    public string? NoTrackingMethodName { get; internal set; } = "AsNoTracking";

    public string? TrackingMethodName { get; internal set; } = "AsTracking";

    public string? NoTrackingWithIdentityResolutionMethodName { get; internal set; } = "AsNoTrackingWithIdentityResolution";
}
