namespace RonSijm.RepoGen.Design;

public sealed class RepositoryGenerationOptionsBuilder
{
    private readonly RepositoryGenerationOptions options;

    internal RepositoryGenerationOptionsBuilder(RepositoryGenerationOptions options)
    {
        this.options = options;
    }

    /// <summary>Direct-context CRUD without generated cache, range, retry, transaction, set-based, or tracking-method helpers.</summary>
    /// <remarks>Apply before individual overrides. Does not change contract namespace, entity selection, or tracking policy.</remarks>
    public RepositoryGenerationOptionsBuilder UseSimpleDefaults() => UseProfile(RepositoryGenerationProfile.Simple);

    /// <summary>Restores the existing selector-generation defaults; context resolution remains opt-in.</summary>
    public RepositoryGenerationOptionsBuilder UseCompatibilityDefaults() => UseProfile(RepositoryGenerationProfile.Compatibility);

    public RepositoryGenerationOptionsBuilder UseProfile(RepositoryGenerationProfile profile)
    {
        if (profile != RepositoryGenerationProfile.Simple && profile != RepositoryGenerationProfile.Compatibility)
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        var compatibility = profile == RepositoryGenerationProfile.Compatibility;
        options.UseDbContextResolver = false;
        options.UseAsyncContextLeases = false;
        options.GenerateCacheDecorator = compatibility;
        options.GenerateBatchOperations = compatibility;
        options.GenerateConcurrencyRetry = compatibility;
        options.GenerateTransactions = compatibility;
        options.GenerateSetBasedOperations = false;
        options.GenerateSynchronousMethods = true;
        options.GenerateTrackingMethods = compatibility;
        return this;
    }

    public RepositoryGenerationOptionsBuilder UseDbContextResolver(bool enabled = true)
    {
        options.UseDbContextResolver = enabled;
        return this;
    }

    /// <summary>Places generated persistence types and selector infrastructure in an explicit namespace.</summary>
    /// <remarks>Opt in when several contexts generate into one assembly, especially when they share entity CLR types.
    /// Entity/DTO types and closed contract namespaces are not renamed. Null restores compatibility placement.</remarks>
    public RepositoryGenerationOptionsBuilder UseGeneratedNamespace(string? generatedNamespace)
    {
        if (generatedNamespace is not null && string.IsNullOrWhiteSpace(generatedNamespace))
        {
            throw new ArgumentException("A nonempty generated namespace is required, or null to restore the default.", nameof(generatedNamespace));
        }

        options.GeneratedNamespace = generatedNamespace;
        return this;
    }

    /// <summary>Generates EF-free read/write ports containing only configured named operations.</summary>
    public RepositoryGenerationOptionsBuilder UseClosedRepositories(string contractNamespace)
    {
        if (string.IsNullOrWhiteSpace(contractNamespace))
        {
            throw new ArgumentException("A contract namespace is required.", nameof(contractNamespace));
        }

        options.ContractNamespace = contractNamespace;
        return this;
    }

    /// <summary>Generates separate read/write selector ports. Read selectors have no mutation or query-escape APIs.</summary>
    /// <remarks>Opt-in alternative to closed repositories. Uses the existing borrowed context; no additional lifetime infrastructure is required.</remarks>
    public RepositoryGenerationOptionsBuilder UseCqrsSelectors(bool enabled = true)
    {
        options.CqrsSelectors = enabled;
        return this;
    }

    /// <summary>Generates separate, EF-free scripted substitutes for closed ports. Null disables generation.</summary>
    /// <remarks>Off by default. Requires a separate TestDoubles output location and the optional RonSijm.RepoGen.Testing runtime.
    /// All selected operations require an explicit test arrangement; no persistence or business behavior is inferred.</remarks>
    public RepositoryGenerationOptionsBuilder UseScriptedTestDoubles(string? testingNamespace)
    {
        if (testingNamespace is not null && string.IsNullOrWhiteSpace(testingNamespace))
        {
            throw new ArgumentException("A nonempty testing namespace is required, or null to disable generation.", nameof(testingNamespace));
        }

        options.ScriptedTestDoubleNamespace = testingNamespace;
        return this;
    }

    /// <summary>Emits a shared test-only fixture for explicit HasInMemoryStore declarations.</summary>
    /// <remarks>Off by default. Requires closed/scripted test doubles and at least one selected store.
    /// Storage is arrangement infrastructure; operation scripts remain required until a portable backend is selected.</remarks>
    public RepositoryGenerationOptionsBuilder UseInMemoryTestStores(bool enabled = true)
    {
        options.InMemoryTestStores = enabled;
        return this;
    }

    /// <summary>Executes portable designer-defined reads against explicit in-memory fixture stores.</summary>
    /// <remarks>Off by default. Requires UseInMemoryTestStores. Uses detached LINQ-to-Objects semantics, not SQL translation,
    /// tracking, caching or relational guarantees. Script arrangements take precedence over generated execution.
    /// Unsupported reads fail with a coverage reason unless explicitly scripted.</remarks>
    public RepositoryGenerationOptionsBuilder UseInMemoryTestQueries(bool enabled = true)
    {
        options.InMemoryTestQueries = enabled;
        return this;
    }

    /// <summary>Rejects non-portable test reads at generation time unless RequireTestOverride explicitly acknowledges them.</summary>
    /// <remarks>Requires UseInMemoryTestQueries. Command coverage is configured independently.</remarks>
    public RepositoryGenerationOptionsBuilder RequirePortableTestQueries(bool enabled = true)
    {
        options.RequirePortableTestQueries = enabled;
        return this;
    }

    /// <summary>Simulates supported designed tracked writes and shared Stage/Save against fixture stores.</summary>
    /// <remarks>Off by default. Requires UseInMemoryTestStores and explicit entity-shaped scalar mappings.
    /// Creates require caller-supplied values or explicit generated-value adapters; patches/deletes, including bounded batches, can use known
    /// existing generated-on-insert values. Scalar immediate Update/Delete/InsertIfAbsent/InsertOrUpdate needs per-command SimulateImmediateWrites.
    /// No SQL constraints, value generation, provider concurrency or cache behavior is inferred.</remarks>
    public RepositoryGenerationOptionsBuilder UseInMemoryTestCommands(bool enabled = true)
    {
        options.InMemoryTestCommands = enabled;
        return this;
    }

    /// <summary>Rejects non-portable commands unless RequireTestOverride acknowledges a scripted implementation.</summary>
    public RepositoryGenerationOptionsBuilder RequirePortableTestCommands(bool enabled = true)
    {
        options.RequirePortableTestCommands = enabled;
        return this;
    }

    /// <summary>Requires review receipts when configured expressions/load paths traverse entities with RepoGen visibility policies.</summary>
    /// <remarks>Off by default. This validates design-time declarations; it does not inject navigation filters or enforce authorization.</remarks>
    public RepositoryGenerationOptionsBuilder RequireNavigationVisibilityReview(bool enabled = true)
    {
        options.RequireNavigationVisibilityReview = enabled;
        return this;
    }

    /// <summary>Includes public request/result data shapes in optional API snapshots. Requires closed repositories; does not create an output file.</summary>
    public RepositoryGenerationOptionsBuilder IncludeApiDataContracts(bool enabled = true)
    {
        options.IncludeApiDataContracts = enabled;
        return this;
    }

    /// <summary>Uses asynchronous context leases for closed operations. Requires UseClosedRepositories.</summary>
    public RepositoryGenerationOptionsBuilder UseAsyncContextLeases(bool enabled = true)
    {
        options.UseAsyncContextLeases = enabled;
        return this;
    }

    /// <summary>Enables scope-aware closed-operation caching. Leases must supply ScopeKey unless a fixed single-database/visibility partition is explicitly configured.</summary>
    public RepositoryGenerationOptionsBuilder UseClosedCaching(string? singleContextScope = null)
    {
        if (singleContextScope is not null && string.IsNullOrWhiteSpace(singleContextScope))
        {
            throw new ArgumentException("A nonempty cache partition is required.", nameof(singleContextScope));
        }

        options.ClosedCaching = true;
        options.SingleContextCacheScope = singleContextScope;
        options.GenerateCacheDecorator = true;
        return this;
    }

    /// <summary>Explicitly disables closed caching, including its constructor dependency, without rewriting query declarations.</summary>
    public RepositoryGenerationOptionsBuilder UseClosedCaching(bool enabled, string? singleContextScope = null)
    {
        if (enabled)
        {
            return UseClosedCaching(singleContextScope);
        }

        options.ClosedCaching = false;
        options.SingleContextCacheScope = null;
        options.GenerateCacheDecorator = false;
        return this;
    }

    /// <summary>Resolves closed operations from a typed request key. Every operation must declare UseContextKey.</summary>
    public RepositoryGenerationOptionsBuilder UseKeyedContextLeases<TKey>()
    {
        options.ContextKeyType = typeof(TKey);
        options.UseAsyncContextLeases = true;
        return this;
    }

    /// <summary>Opts in to a provider-specific atomic insert capability for named closed commands.</summary>
    public RepositoryGenerationOptionsBuilder UseAtomicWrites<TWriter>(bool enabled = true)
    {
        options.AtomicWriterType = enabled ? typeof(TWriter) : null;
        return this;
    }

    /// <summary>Opts in to a provider-specific UPDATE/DELETE RETURNING capability for named closed commands.</summary>
    public RepositoryGenerationOptionsBuilder UseReturningWrites<TWriter>(bool enabled = true)
    {
        options.ReturningWriterType = enabled ? typeof(TWriter) : null;
        return this;
    }

    /// <summary>Sets the default SQL-command timeout for named closed operations. Null removes this model default.</summary>
    public RepositoryGenerationOptionsBuilder UseCommandTimeout(int? seconds)
    {
        if (seconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        options.CommandTimeoutSeconds = seconds;
        return this;
    }

    public RepositoryGenerationOptionsBuilder GenerateOnlyConfiguredEntities(bool enabled = true)
    {
        options.GenerateOnlyConfiguredEntities = enabled;
        return this;
    }

    public RepositoryGenerationOptionsBuilder GenerateCacheDecorator(bool enabled = true)
    {
        options.GenerateCacheDecorator = enabled;
        return this;
    }

    public RepositoryGenerationOptionsBuilder GenerateBatchOperations(bool enabled = true)
    {
        options.GenerateBatchOperations = enabled;
        return this;
    }

    public RepositoryGenerationOptionsBuilder GenerateSetBasedOperations(bool enabled = true)
    {
        options.GenerateSetBasedOperations = enabled;
        return this;
    }

    public RepositoryGenerationOptionsBuilder GenerateConcurrencyRetry(bool enabled = true)
    {
        options.GenerateConcurrencyRetry = enabled;
        return this;
    }

    public RepositoryGenerationOptionsBuilder GenerateTransactions(bool enabled = true)
    {
        options.GenerateTransactions = enabled;
        return this;
    }

    public RepositoryGenerationOptionsBuilder GenerateSynchronousMethods(bool enabled = true)
    {
        options.GenerateSynchronousMethods = enabled;
        return this;
    }

    public RepositoryGenerationOptionsBuilder UseDefaultTracking(RepositoryTrackingBehavior behavior)
    {
        ValidateTrackingBehavior(behavior);
        options.DefaultTrackingBehavior = behavior;
        return this;
    }

    /// <summary>Sets the default for closed and CQRS-selector reads, independently of write tracking.</summary>
    /// <remarks>Defaults to NoTracking. An explicit query policy takes precedence; Unspecified inherits UseDefaultTracking or the context policy.</remarks>
    public RepositoryGenerationOptionsBuilder UseReadOnlyTracking(RepositoryTrackingBehavior behavior)
    {
        ValidateTrackingBehavior(behavior);
        options.ReadOnlyTrackingBehavior = behavior;
        return this;
    }

    /// <summary>Controls whether generated selectors expose runtime tracking controls. Designer policies remain effective.</summary>
    public RepositoryGenerationOptionsBuilder GenerateTrackingMethods(bool enabled = true)
    {
        options.GenerateTrackingMethods = enabled;
        return this;
    }

    /// <summary>Names the generated tracking controls; null omits that individual method.</summary>
    /// <remarks>Does not enable methods disabled by GenerateTrackingMethods(false) or UseSimpleDefaults().</remarks>
    public RepositoryGenerationOptionsBuilder UseTrackingMethodNames(
        string? noTracking = "AsNoTracking",
        string? tracking = "AsTracking",
        string? noTrackingWithIdentityResolution = "AsNoTrackingWithIdentityResolution")
    {
        options.NoTrackingMethodName = noTracking;
        options.TrackingMethodName = tracking;
        options.NoTrackingWithIdentityResolutionMethodName = noTrackingWithIdentityResolution;
        return this;
    }

    private static void ValidateTrackingBehavior(RepositoryTrackingBehavior behavior)
    {
        if (behavior is < RepositoryTrackingBehavior.Unspecified or > RepositoryTrackingBehavior.NoTrackingWithIdentityResolution)
        {
            throw new ArgumentOutOfRangeException(nameof(behavior));
        }
    }
}
