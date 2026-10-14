using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;
using RepositoryDesignModel = RonSijm.RepoGen.Design.RepositoryDesign;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    public static RepositoryGenerationModel Create(
        DbContext context,
        Assembly targetAssembly,
        bool generateSelectors = false,
        RepositoryDesignModel? design = null)
    {
        var contextTypeName = CSharpTypeName(context.GetType());
        var configuredOptions = design?.Options ?? new RepositoryGenerationOptions();
        if (configuredOptions.InMemoryTestCommands && !configuredOptions.InMemoryTestStores ||
            configuredOptions.RequirePortableTestCommands && !configuredOptions.InMemoryTestCommands)
            throw new InvalidOperationException("RGD3401: UseInMemoryTestCommands requires UseInMemoryTestStores; RequirePortableTestCommands requires UseInMemoryTestCommands.");
        if (configuredOptions.InMemoryTestQueries && !configuredOptions.InMemoryTestStores ||
            configuredOptions.RequirePortableTestQueries && !configuredOptions.InMemoryTestQueries)
            throw new InvalidOperationException("RGD3301: UseInMemoryTestQueries requires UseInMemoryTestStores; RequirePortableTestQueries requires UseInMemoryTestQueries.");
        if (configuredOptions.InMemoryTestStores && (configuredOptions.ScriptedTestDoubleNamespace is null || configuredOptions.ContractNamespace is null))
            throw new InvalidOperationException("RGD3201: UseInMemoryTestStores requires closed repositories and UseScriptedTestDoubles with an explicit testing namespace.");
        if (configuredOptions.ScriptedTestDoubleNamespace is not null && configuredOptions.ContractNamespace is null ||
            configuredOptions.ScriptedTestDoubleNamespace is null && design?.Entities.Any(static entity => entity.RepositoryEnabled && entity.TestDoubleEnabled == true) == true)
        {
            throw new InvalidOperationException("RGD3101: Scripted test doubles require UseClosedRepositories and UseScriptedTestDoubles with an explicit testing namespace.");
        }
        if (configuredOptions.CqrsSelectors && (configuredOptions.ContractNamespace is not null || context.GetType().IsGenericType))
        {
            throw new InvalidOperationException("RGD1601: UseCqrsSelectors requires a non-generic context and cannot be combined with UseClosedRepositories. Choose one public surface.");
        }

        if (configuredOptions.IncludeApiDataContracts && configuredOptions.ContractNamespace is null)
        {
            throw new InvalidOperationException("RGA0003: Data-contract API snapshots require closed repositories.");
        }

        if (configuredOptions.CommandTimeoutSeconds is not null && configuredOptions.ContractNamespace is null)
        {
            throw new InvalidOperationException("Operation-local command timeouts require closed repositories so query/stream lifetimes can restore a borrowed context safely.");
        }

        if (configuredOptions.AtomicWriterType is { } atomicWriter)
        {
            if (configuredOptions.ContractNamespace is null || !atomicWriter.IsVisible || atomicWriter.IsAbstract || atomicWriter.ContainsGenericParameters ||
                !atomicWriter.GetInterfaces().Any(static type => type.FullName == "RonSijm.RepoGen.EntityFrameworkCore.IRepositoryAtomicWriter"))
            {
                throw new InvalidOperationException("RGD1301: UseAtomicWrites<TWriter> requires a public concrete IRepositoryAtomicWriter and closed repositories.");
            }

            if (atomicWriter.GetCustomAttribute<RepositoryProviderAttribute>() is { } requirement && requirement.ProviderName != context.Database.ProviderName)
            {
                throw new InvalidOperationException($"RGD1302: Atomic writer requires '{requirement.ProviderName}', but the context uses '{context.Database.ProviderName}'.");
            }
        }

        if (configuredOptions.ReturningWriterType is { } returningWriter)
        {
            if (configuredOptions.ContractNamespace is null || !returningWriter.IsVisible || returningWriter.IsAbstract || returningWriter.ContainsGenericParameters ||
                !returningWriter.GetInterfaces().Any(static type => type.FullName == "RonSijm.RepoGen.EntityFrameworkCore.IRepositoryReturningWriter"))
            {
                throw new InvalidOperationException("RGD1305: UseReturningWrites<TWriter> requires a public concrete IRepositoryReturningWriter and closed repositories.");
            }

            if (returningWriter.GetCustomAttribute<RepositoryProviderAttribute>() is { } requirement && requirement.ProviderName != context.Database.ProviderName)
            {
                throw new InvalidOperationException($"RGD1302: Returning writer requires '{requirement.ProviderName}', but the context uses '{context.Database.ProviderName}'.");
            }
        }

        foreach (var definition in design?.Entities ?? [])
        {
            CreateVisibilityPolicies(definition);
        }

        if (configuredOptions.ClosedCaching && configuredOptions.GenerateCacheDecorator && (configuredOptions.ContractNamespace is null ||
                                                                                            !configuredOptions.UseAsyncContextLeases && configuredOptions.SingleContextCacheScope is null))
        {
            throw new InvalidOperationException("RGD1203: UseClosedCaching requires closed repositories and scoped async leases, or an explicit singleContextScope.");
        }

        if (configuredOptions.ContextKeyType is not null)
        {
            if (configuredOptions.ContractNamespace is null || !configuredOptions.UseAsyncContextLeases)
            {
                throw new InvalidOperationException("RGD1202: Keyed context selection requires closed async context leases.");
            }

            foreach (var definition in design!.Entities.Where(static definition => definition.RepositoryEnabled))
            {
                foreach (var operation in definition.Queries.Where(query => query.Target == RepositoryQueryTarget.Write ? definition.WriteEnabled : definition.ReadEnabled).Select(static query => (query.MethodName, query.ContextKey))
                             .Concat(definition.Commands.Where(_ => definition.WriteEnabled).Select(static command => (command.MethodName, command.ContextKey))))
                {
                    if (operation.ContextKey?.ReturnType != configuredOptions.ContextKeyType)
                    {
                        throw new InvalidOperationException($"RGD1202: Operation '{operation.MethodName}' must declare UseContextKey with '{configuredOptions.ContextKeyType.FullName}'.");
                    }
                }
            }
        }
        else if (design?.Entities.Where(static definition => definition.RepositoryEnabled).Any(definition =>
                     definition.Queries.Any(query => query.ContextKey is not null && (configuredOptions.ContractNamespace is null && !configuredOptions.CqrsSelectors ||
                                                                                      (query.Target == RepositoryQueryTarget.Write ? definition.WriteEnabled : definition.ReadEnabled))) ||
                     definition.Commands.Any(command => command.ContextKey is not null && (configuredOptions.ContractNamespace is null && !configuredOptions.CqrsSelectors || definition.WriteEnabled))) == true)
        {
            throw new InvalidOperationException("RGD1202: UseContextKey requires UseKeyedContextLeases<TKey> in designer options.");
        }

        if (configuredOptions.UseAsyncContextLeases && configuredOptions.ContractNamespace is null)
        {
            throw new InvalidOperationException("RGD1201: Async context leases require UseClosedRepositories so owned contexts cannot escape through selectors.");
        }

        if (configuredOptions.ContractNamespace is not null && configuredOptions.UseDbContextResolver)
        {
            throw new InvalidOperationException("RGD1201: Closed repositories use direct injection or async context leases, not the synchronous selector resolver.");
        }

        if (configuredOptions.ContractNamespace is not null && context.GetType().IsGenericType)
        {
            throw new InvalidOperationException("Closed repository registration currently requires a non-generic DbContext type.");
        }

        var settings = new RepositoryGenerationSettingsModel(
            configuredOptions.UseDbContextResolver,
            configuredOptions.GenerateCacheDecorator,
            configuredOptions.GenerateBatchOperations,
            configuredOptions.GenerateSetBasedOperations,
            configuredOptions.GenerateConcurrencyRetry,
            configuredOptions.GenerateTransactions,
            configuredOptions.GenerateSynchronousMethods,
            configuredOptions.DefaultTrackingBehavior,
            configuredOptions.ContractNamespace,
            configuredOptions.UseAsyncContextLeases,
            configuredOptions.ReadOnlyTrackingBehavior,
            configuredOptions.GenerateTrackingMethods,
            configuredOptions.NoTrackingMethodName,
            configuredOptions.TrackingMethodName,
            configuredOptions.NoTrackingWithIdentityResolutionMethodName,
            configuredOptions.ClosedCaching && configuredOptions.GenerateCacheDecorator,
            configuredOptions.SingleContextCacheScope,
            configuredOptions.ContextKeyType is null ? null : CSharpTypeName(configuredOptions.ContextKeyType),
            configuredOptions.AtomicWriterType is null ? null : CSharpTypeName(configuredOptions.AtomicWriterType),
            configuredOptions.CommandTimeoutSeconds,
            configuredOptions.ReturningWriterType is null ? null : CSharpTypeName(configuredOptions.ReturningWriterType),
            configuredOptions.CqrsSelectors,
            configuredOptions.GeneratedNamespace,
            configuredOptions.RequireNavigationVisibilityReview,
            configuredOptions.ScriptedTestDoubleNamespace,
            configuredOptions.InMemoryTestStores,
            configuredOptions.InMemoryTestQueries,
            configuredOptions.RequirePortableTestQueries, configuredOptions.InMemoryTestCommands, configuredOptions.RequirePortableTestCommands);
        RepositoryGeneratedTypes.ValidateNamespace(settings.GeneratedNamespace);
        RepositoryGeneratedTypes.ValidateNamespace(settings.ScriptedTestDoubleNamespace);
        ValidateInMemoryNavigationDeclarations(context.Model, design, settings);
        var inMemoryRelationshipMetadata = design?.Entities.Any(entity => entity.RepositoryEnabled &&
            entity.TestDoubleEnabled != false && (entity.ReadEnabled || entity.WriteEnabled) &&
            (settings.InMemoryTestQueries && entity.InMemoryNavigations.Count != 0 || settings.InMemoryTestCommands &&
                (entity.Commands.Count != 0 || entity.InMemoryStore is { ModelDefaultsEnabled: true } || entity.InMemoryStore?.GeneratedValues.Count > 0))) == true
            ? context.GetService<IDesignTimeModel>().Model : null;
        ValidateTrackingMethodNames(settings);
        var entities = context.Model.GetEntityTypes()
            .Where(static entity =>
                !entity.IsOwned() &&
                entity.ClrType is { IsGenericType: false, IsNested: false })
            .Where(entity =>
                design?.Entities.FirstOrDefault(item => item.EntityType == entity.ClrType)?.RepositoryEnabled != false &&
                (!configuredOptions.GenerateOnlyConfiguredEntities ||
                 design?.Entities.Any(item => item.EntityType == entity.ClrType) == true))
            .OrderBy(static entity => entity.ClrType.FullName, StringComparer.Ordinal)
            .Select(entity => CreateEntity(entity, contextTypeName, targetAssembly, generateSelectors, design, settings, context.Database.ProviderName, inMemoryRelationshipMetadata))
            .ToArray();

        if (entities.Length == 0)
        {
            throw new InvalidOperationException(
                $"{context.GetType().FullName} does not contain any enabled supported entities.");
        }

        if (settings.InMemoryTestStores && !entities.Any(static entity => entity.GenerateTestDouble && (entity.ReadEnabled || entity.WriteEnabled) && entity.InMemoryStore is not null))
            throw new InvalidOperationException("RGD3201: UseInMemoryTestStores requires HasInMemoryStore on at least one selected test-double entity.");

        if (settings.CqrsSelectors)
        {
            ValidateCqrsTypeNames(entities, context.Model, settings);
        }

        var finalizedEntities = entities.Select(entity =>
        {
            var invalidationTags = new HashSet<string>(entity.CacheInvalidationTags, StringComparer.Ordinal)
            {
                entity.CacheTag
            };
            var affectedTypes = new HashSet<string>(StringComparer.Ordinal) { entity.EntityTypeName };
            foreach (var collection in entity.Commands.SelectMany(static command => command.Collections ?? []))
            {
                invalidationTags.Add(collection.ChildCacheTag);
                affectedTypes.Add(collection.ChildTypeName);
                foreach (var childRepository in entities.Where(candidate => candidate.EntityTypeName == collection.ChildTypeName))
                {
                    invalidationTags.Add(childRepository.CacheTag);
                }
            }
            foreach (var owner in entities)
            {
                if (owner.Queries.Any(query =>
                        query.Cache.Enabled &&
                        query.Cache.DependentEntityTypeNames.Any(affectedTypes.Contains)))
                {
                    invalidationTags.Add(owner.CacheTag);
                }
            }

            return entity with
            {
                CacheInvalidationTags = invalidationTags.OrderBy(static tag => tag, StringComparer.Ordinal).ToArray()
            };
        }).ToArray();

        var dataContracts = configuredOptions.IncludeApiDataContracts
            ? RepositoryApiDataContractCapture.Capture(finalizedEntities.SelectMany(static entity =>
                entity.Queries.SelectMany(static query => query.ApiDataTypes ?? [])
                    .Concat(entity.Commands.SelectMany(static command => command.ApiDataTypes ?? []))
                    .Concat((entity.ReadPorts ?? []).Concat(entity.WritePorts ?? []).SelectMany(static port => port.ApiDataTypes ?? []))))
            : null;
        var model = new RepositoryGenerationModel(contextTypeName, finalizedEntities, settings, dataContracts);
        return model with { NavigationVisibility = RepositoryNavigationVisibilityValidator.Create(context.Model, design, model) };
    }

    private static EntityRepositoryModel CreateEntity(
        IEntityType entity,
        string contextTypeName,
        Assembly targetAssembly,
        bool generateSelectors,
        RepositoryDesignModel? design,
        RepositoryGenerationSettingsModel settings, string? providerName, IModel? inMemoryRelationshipMetadata)
    {
        var entityType = entity.ClrType;
        var entityDesign = design?.Entities.FirstOrDefault(item => item.EntityType == entityType);
        var excludedProperties = entityDesign?.ExcludedProperties is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(entityDesign.ExcludedProperties, StringComparer.Ordinal);
        var explicitProperties = entityDesign?.Accessors
            .Select(static accessor => accessor.PropertyName)
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        var lookups = new List<LookupModel>();
        var propertySets = new HashSet<string>(StringComparer.Ordinal);

        AddLookup(
            entity.FindPrimaryKey()?.Properties,
            isUnique: true,
            methodName: null,
            lookups,
            propertySets,
            excludedProperties,
            explicitProperties, entityDesign);
        foreach (var index in entity.GetIndexes()
                     .OrderBy(static index => string.Join("|", index.Properties.Select(static property => property.Name)), StringComparer.Ordinal))
        {
            AddLookup(
                index.Properties,
                index.IsUnique,
                methodName: null,
                lookups,
                propertySets,
                excludedProperties,
                explicitProperties, entityDesign);
        }

        if (entityDesign is not null)
        {
            foreach (var accessor in entityDesign.Accessors)
            {
                var property = entity.FindProperty(accessor.PropertyName)
                               ?? throw new InvalidOperationException(
                                   $"Configured accessor '{entityType.FullName}.{accessor.PropertyName}' is not part of the EF model.");
                if (!string.IsNullOrWhiteSpace(accessor.MethodName))
                {
                    var inferredLookup = lookups.FirstOrDefault(lookup =>
                        lookup.Properties.Count == 1 &&
                        string.Equals(lookup.Properties[0].PropertyName, property.Name, StringComparison.Ordinal));
                    if (inferredLookup is not null)
                    {
                        lookups.Remove(inferredLookup);
                        propertySets.Remove(property.Name);
                    }
                }

                AddLookup(
                    [property],
                    accessor.IsUnique || !accessor.UniquenessConfigured && accessor.UseInferredUniqueness &&
                    (entity.GetKeys().Any(key => key.Properties.Count == 1 && key.Properties[0] == property) ||
                     entity.GetIndexes().Any(index => index.IsUnique && index.Properties.Count == 1 && index.Properties[0] == property)),
                    accessor.MethodName,
                    lookups,
                    propertySets,
                    excludedProperties,
                    explicitProperties: null, entityDesign);
            }
        }

        var persistenceNamespace = settings.GeneratedNamespace ?? entityType.Namespace;
        var selectorFullName = string.IsNullOrEmpty(persistenceNamespace)
            ? entityType.Name + "Selector"
            : persistenceNamespace + "." + entityType.Name + "Selector";
        var selectorExists = targetAssembly.GetType(selectorFullName, throwOnError: false, ignoreCase: false) is not null;
        if (settings.GeneratedNamespace is not null && selectorExists && !generateSelectors)
        {
            throw new InvalidOperationException($"RGD1802: Namespace-isolated selectors for '{entityType.FullName}' must be tool-generated. Enable generateSelectors (or --generate-selectors) rather than reusing unrelated selector infrastructure.");
        }

        if (settings.CqrsSelectors && selectorExists && (!generateSelectors || entityType.GetCustomAttributesData().Any(static attribute =>
                attribute.AttributeType.FullName == "RonSijm.RepoGen.GenerateSelectorsAttribute")))
        {
            throw new InvalidOperationException($"RGD1602: CQRS selectors for '{entityType.FullName}' must be tool-generated. Remove GenerateSelectors attributes and regenerate with --generate-selectors when existing selectors are present.");
        }

        var projectionEnabled = entityDesign?.ProjectionEnabled ?? true;
        var customTrackingSurface = !settings.GenerateTrackingMethods || settings.NoTrackingMethodName != "AsNoTracking" ||
                                    settings.TrackingMethodName != "AsTracking" ||
                                    settings.NoTrackingWithIdentityResolutionMethodName != "AsNoTrackingWithIdentityResolution";
        if (selectorExists && customTrackingSurface && entityType.GetCustomAttributesData().Any(static attribute =>
                attribute.AttributeType.FullName == "RonSijm.RepoGen.GenerateSelectorsAttribute"))
        {
            throw new InvalidOperationException(
                $"Entity '{entityType.FullName}' customizes tracking methods but has attribute-generated selectors. " +
                "Remove GenerateSelectors and rebuild the schema before generating the designer-configured selector surface.");
        }

        if (selectorExists && !generateSelectors && customTrackingSurface)
        {
            throw new InvalidOperationException(
                $"Entity '{entityType.FullName}' customizes tracking methods but reuses existing selectors. " +
                "Regenerate with --generate-selectors so RepoGen can emit the configured surface; " +
                "remove GenerateSelectors attributes before rebuilding if those selectors are attribute-generated.");
        }

        if (!projectionEnabled && selectorExists && !generateSelectors)
        {
            throw new InvalidOperationException(
                $"Entity '{entityType.FullName}' disables ad-hoc projections. Regenerate with --generate-selectors " +
                "so RepoGen can emit the restricted selector surface.");
        }

        var filters = CreateFilters(entityDesign);
        ValidateMethodSignatures(entityType, lookups, filters);
        if (settings.ContractNamespace is not null)
        {
            ClosedRepositorySourceRenderer.ValidateEntity(entity, entityDesign, settings.GenerateCacheDecorator && !settings.ClosedCaching);
            if (selectorExists && entityType.GetCustomAttributesData().Any(static attribute =>
                    attribute.AttributeType.FullName == "RonSijm.RepoGen.GenerateSelectorsAttribute"))
            {
                throw new InvalidOperationException($"Entity '{entityType.FullName}' has attribute-generated selectors. Remove GenerateSelectors and rebuild the schema before selecting the internal closed-repository surface.");
            }

            if (selectorExists && !generateSelectors)
            {
                throw new InvalidOperationException("Closed repositories require --generate-selectors when selectors already exist, so the persistence surface remains internal.");
            }
        }

        if (settings.ContractNamespace is null && entityDesign is not null &&
            ((!settings.CqrsSelectors && (!entityDesign.ReadEnabled || !entityDesign.WriteEnabled)) || entityDesign.ReadPorts.Count > 0 || entityDesign.WritePorts.Count > 0))
        {
            throw new InvalidOperationException("ReadEnabled/WriteEnabled require UseClosedRepositories or UseCqrsSelectors. Application-port bindings require UseClosedRepositories; the compatibility selector surface remains combined.");
        }

        if (settings.CqrsSelectors && entity.FindPrimaryKey() is null && entityDesign is { WriteEnabled: true, Commands.Count: > 0 })
        {
            throw new InvalidOperationException($"RGD1604: Keyless entity '{entityType.FullName}' cannot configure write commands. Remove them or explicitly disable the write capability.");
        }

        if (entityDesign?.Queries.Any(static query => query.Target == RepositoryQueryTarget.Write) == true)
        {
            if (settings.ContractNamespace is null && !settings.CqrsSelectors)
            {
                throw new InvalidOperationException("RGD2101: OnWriteRepository requires UseClosedRepositories or UseCqrsSelectors. The compatibility surface has no separate query capabilities.");
            }

            if (entity.FindPrimaryKey() is null && entityDesign.WriteEnabled)
            {
                throw new InvalidOperationException($"RGD2102: Keyless entity '{entityType.FullName}' has no write repository for a command-side query. Use the read surface or explicitly disable the write capability.");
            }
        }

        var protectedWrites = CreateWriteProtection(entity, entityDesign, settings);
        var result = new EntityRepositoryModel(
            persistenceNamespace ?? string.Empty,
            settings.ContractNamespace is null && entityType.IsPublic ? "public" : "internal",
            entityType.Name,
            CSharpTypeName(entityType),
            contextTypeName,
            generateSelectors || !selectorExists,
            projectionEnabled,
            lookups,
            filters,
            CreateDefaultSorts(entityDesign),
            CreateSortableFields(entity, entityDesign, settings),
            CreateIncludes(entity, entityDesign),
            CreateProjections(entity, entityDesign).Select(projection => settings.GeneratedNamespace is null ? projection : projection with { Namespace = settings.GeneratedNamespace }).ToArray(),
            CreateConfiguredQueries(entity, entityDesign, settings, design, contextTypeName, providerName, inMemoryRelationshipMetadata),
            (settings.ContractNamespace is not null || settings.CqrsSelectors) && (entityDesign?.WriteEnabled == false || entity.FindPrimaryKey() is null) ? [] : CreateConfiguredCommands(entity, entityDesign, settings, providerName, design, protectedWrites, inMemoryRelationshipMetadata),
            settings.GeneratedNamespace is null ? entityType.FullName ?? entityType.Name : contextTypeName + ":" + (entityType.FullName ?? entityType.Name),
            [settings.GeneratedNamespace is null ? entityType.FullName ?? entityType.Name : contextTypeName + ":" + (entityType.FullName ?? entityType.Name)],
            entityDesign?.ReadEnabled ?? true,
            entity.FindPrimaryKey() is not null && (entityDesign?.WriteEnabled ?? true),
            LoadProfiles: entityDesign?.LoadProfiles.Select(profile => new NamedLoadProfileModel(profile.Name,
                RepositoryIncludeSourceRenderer.Create(entity, profile.Includes, new List<FilterParameterModel>()))).ToArray(),
            VisibilityPolicies: CreateVisibilityPolicies(entityDesign),
            CommandTimeout: ResolveCommandTimeout(settings, entityDesign?.CommandTimeout, null, entityType.FullName ?? entityType.Name),
            ProtectedWriteProperties: protectedWrites.Length == 0 ? null : protectedWrites,
            GenerateTestDouble: settings.ScriptedTestDoubleNamespace is not null && entityDesign?.TestDoubleEnabled != false,
            InMemoryStore: CreateInMemoryStore(entity, entityDesign, settings, design, inMemoryRelationshipMetadata));
        ValidateEntityTrackingMethodNames(result, settings);
        if (settings.CqrsSelectors)
        {
            ClosedRepositorySourceRenderer.ValidateOperationParameters(result);
            ValidateCqrsCommandReadMembers(result, settings);
            ValidateCqrsSelectorProjections(result);
        }

        if (settings.ContractNamespace is null)
        {
            return result;
        }

        ClosedRepositorySourceRenderer.ValidateOperationParameters(result);
        return RepositoryPortBinding.Apply(result, entityDesign);
    }

    private static IEnumerable<string> TrackingMethodNames(RepositoryGenerationSettingsModel settings) =>
        new[] { settings.NoTrackingMethodName, settings.TrackingMethodName, settings.NoTrackingWithIdentityResolutionMethodName }
            .OfType<string>();

    private static void ValidateTrackingMethodNames(RepositoryGenerationSettingsModel settings)
    {
        if (!settings.GenerateTrackingMethods)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in TrackingMethodNames(settings))
        {
            if (name.Length == 0 || (!char.IsLetter(name[0]) && name[0] != '_') ||
                name.Any(static character => !char.IsLetterOrDigit(character) && character != '_') ||
                EscapeIdentifier(name) != name || name is "__arglist" or "__makeref" or "__reftype" or "__refvalue")
            {
                throw new InvalidOperationException($"Tracking method name '{name}' is not a supported C# identifier. Use a method name, or null to omit it.");
            }

            if (!names.Add(name))
            {
                throw new InvalidOperationException($"Duplicate tracking method name '{name}'. Use distinct names, or null to omit individual methods.");
            }

            if (ReservedSelectorMemberNames.Contains(name))
            {
                throw new InvalidOperationException($"Tracking method name '{name}' conflicts with a selector member. Choose a different name.");
            }
        }
    }

    private static void ValidateEntityTrackingMethodNames(EntityRepositoryModel entity, RepositoryGenerationSettingsModel settings)
    {
        if (!settings.GenerateTrackingMethods)
        {
            return;
        }

        foreach (var name in TrackingMethodNames(settings))
        {
            if (name == entity.EntityName + "Selector" || name == entity.EntityName + "CollectionSelector" ||
                settings.CqrsSelectors && (name == "inner" || name == "Read" + entity.EntityName + "Selector" || name == "Read" + entity.EntityName + "CollectionSelector") ||
                entity.Projections.Any(projection => projection.SingleMethodName == name || projection.CollectionMethodName == name))
            {
                throw new InvalidOperationException($"Tracking method name '{name}' conflicts with a selector or projection for entity '{entity.EntityTypeName}'. Choose a different name.");
            }
        }
    }

    private static void ValidateCqrsSelectorProjections(EntityRepositoryModel entity)
    {
        if (!entity.ReadEnabled)
        {
            return;
        }

        foreach (var collection in new[] { false, true })
        {
            var type = "Read" + entity.EntityName + (collection ? "CollectionSelector" : "Selector");
            var terminals = collection
                ? new[] { "ExistsAsync", "CountAsync", "FirstOrDefaultAsync", "AsEntitiesAsync", "StreamAsync" }
                : new[] { "ExistsAsync", "AsEntityAsync" };
            var names = terminals.ToHashSet(StringComparer.Ordinal);
            foreach (var projection in entity.Projections)
            {
                var method = collection ? projection.CollectionMethodName : projection.SingleMethodName;
                if (method == type || method == "inner" || !names.Add(method))
                {
                    throw new InvalidOperationException($"RGD1603: Projection method '{method}' conflicts with a member of '{type}'. Choose a distinct HasProjection method name.");
                }
            }
        }
    }

    private static void ValidateCqrsCommandReadMembers(EntityRepositoryModel entity, RepositoryGenerationSettingsModel settings)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal) { "AddAsync(" + entity.EntityTypeName + ")" };
        if (settings.GenerateBatchOperations)
        {
            reserved.Add("AddRangeAsync(global::System.Collections.Generic.IEnumerable<" + entity.EntityTypeName + ">)");
        }

        if (settings.GenerateTransactions)
        {
            reserved.Add("ExecuteInTransactionAsync(global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>)");
        }

        if (settings.GenerateConcurrencyRetry)
        {
            reserved.Add("SaveChangesAsync(global::System.Int32,global::System.Func<global::Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>)");
        }

        foreach (var query in entity.Queries.Where(static query => query.Target == RepositoryQueryTarget.Write))
        {
            if (reserved.Contains(RepositoryOperationSignature.Query(query)))
            {
                throw new InvalidOperationException($"RGD2104: Command-side query '{query.MethodName}' conflicts with a generated write helper. Choose a distinct operation name or disable that optional helper.");
            }
        }
    }

    private static void ValidateCqrsTypeNames(IReadOnlyList<EntityRepositoryModel> entities, IModel model, RepositoryGenerationSettingsModel settings)
    {
        var names = model.GetEntityTypes().Select(static entity => entity.ClrType.FullName!).ToHashSet(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            var generated = new List<string>
            {
                entity.EntityName + "Selector", entity.EntityName + "CollectionSelector",
                "I" + entity.EntityName + "Repository", entity.EntityName + "Repository"
            };
            if (settings.GenerateCacheDecorator)
            {
                generated.AddRange(["ICacheable" + entity.EntityName + "Repository", entity.EntityName + "RepositoryCacheDecorator"]);
            }

            if (entity.ReadEnabled)
            {
                generated.AddRange([
                    "IRead" + entity.EntityName + "Repository", entity.EntityName + "ReadRepository",
                    "Read" + entity.EntityName + "Selector", "Read" + entity.EntityName + "CollectionSelector"
                ]);
            }

            if (entity.WriteEnabled)
            {
                generated.Add("IWrite" + entity.EntityName + "Repository");
            }

            foreach (var name in generated)
            {
                var qualified = (entity.Namespace.Length == 0 ? "" : entity.Namespace + ".") + name;
                if (!names.Add(qualified))
                {
                    throw new InvalidOperationException($"RGD1605: CQRS generated type '{qualified}' collides with another entity/generated type. Use distinct entity namespaces or exclude the conflicting entity.");
                }
            }
        }
    }

    private static readonly HashSet<string> ReservedSelectorMemberNames = new(StringComparer.Ordinal)
    {
        "Context", "CacheInvalidator", "Query", "SelectorContext", "ConfigureQuery", "Where", "Include",
        "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Skip", "Take", "GroupBy",
        "Delete", "Remove", "Update", "DeleteOperation", "UpdateOperation", "StageUpdateAsync", "UpdateAsync",
        "MarkForDeletionAsync", "DeleteAsync", "RemoveAsync", "SaveChangesAsync", "ExecuteDeleteAsync", "ExecuteUpdateAsync",
        "AsEntityAsync", "AsEntitiesAsync", "ExistsAsync", "CountAsync", "FirstOrDefaultAsync", "ToDictionaryAsync",
        "ProjectToAsync", "ProjectToListAsync", "ProjectToSingleAsync", "ProjectToSingleOrDefaultAsync", "ProjectToFirstOrDefaultAsync",
        "ToPagedListAsync", "ToCursorPagedListAsync", "StreamAsync", "MaxAsync", "MinAsync", "SumAsync", "AverageAsync",
        "Equals", "GetHashCode", "GetType", "ToString", "MemberwiseClone", "Finalize"
    };

    private static FilterModel[] CreateFilters(RepositoryEntityDesign? design)
    {
        if (design is null)
        {
            return [];
        }

        return design.Filters.Select(filter =>
        {
            var parameterNames = filter.Predicate.Parameters
                .Skip(1)
                .Select((parameter, index) => ToParameterName(parameter.Name ?? "value" + (index + 1)))
                .ToArray();
            var expressionNames = new string[parameterNames.Length + 1];
            expressionNames[0] = "entity";
            parameterNames.CopyTo(expressionNames, 1);
            var parameters = filter.Predicate.Parameters
                .Skip(1)
                .Select((parameter, index) => new FilterParameterModel(
                    parameterNames[index],
                    CSharpTypeName(parameter.Type), RuntimeType: parameter.Type))
                .ToArray();

            return new FilterModel(
                filter.MethodName,
                filter.IsUnique,
                parameters,
                ProjectionExpressionRenderer.RenderBody(filter.Predicate, expressionNames));
        }).ToArray();
    }

    private static SortModel[] CreateDefaultSorts(RepositoryEntityDesign? design) =>
        design?.DefaultSorts
            .Select(sort => new SortModel(
                sort.IsDescending,
                ProjectionExpressionRenderer.Render(sort.KeySelector)))
            .ToArray() ?? [];

    private static SortableFieldModel[] CreateSortableFields(
        IEntityType entity,
        RepositoryEntityDesign? design,
        RepositoryGenerationSettingsModel settings)
    {
        if (design is null)
        {
            return [];
        }

        var configuredFields = design.SortableFields
            .Select(static field => field.PropertyName)
            .Concat(design.Queries
                .Where(query => query.DynamicSort is not null && (settings.ContractNamespace is null && !settings.CqrsSelectors ||
                                                                  (query.Target == RepositoryQueryTarget.Write ? design.WriteEnabled : design.ReadEnabled)))
                .SelectMany(static query => query.DynamicSort!.PropertyNames))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var duplicate = configuredFields
            .GroupBy(static field => field, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Entity '{entity.ClrType.FullName}' configures sortable fields that differ only by casing: '{duplicate.Key}'.");
        }

        return configuredFields.Select(propertyName =>
        {
            _ = entity.FindProperty(propertyName)
                ?? throw new InvalidOperationException(
                    $"Configured sortable field '{entity.ClrType.FullName}.{propertyName}' is not part of the EF model.");
            return new SortableFieldModel(propertyName);
        }).ToArray();
    }

    private static IncludeModel[] CreateIncludes(IEntityType entity, RepositoryEntityDesign? design) =>
        RepositoryIncludeSourceRenderer.Create(entity, design?.Includes ?? []);

    private static void ValidateMethodSignatures(
        Type entityType,
        IReadOnlyList<LookupModel> lookups,
        IReadOnlyList<FilterModel> filters)
    {
        var duplicate = lookups
            .Select(lookup => lookup.MethodName + "(" +
                              string.Join(",", lookup.Properties.Select(static property => property.RuntimeType is null ? property.TypeName : CSharpTypeName(property.RuntimeType))) + ")")
            .Concat(filters.Select(filter => filter.MethodName + "(" +
                                             string.Join(",", filter.Parameters.Select(static parameter => parameter.RuntimeType is null ? parameter.TypeName : CSharpTypeName(parameter.RuntimeType))) + ")"))
            .GroupBy(static signature => signature, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"RGD1705: Repository '{entityType.FullName}' configures duplicate accessor/filter CLR signature '{duplicate.Key}'. Reference annotations cannot distinguish overloads.");
        }
    }

    private static void AddLookup(
        IReadOnlyList<IProperty>? properties,
        bool isUnique,
        string? methodName,
        List<LookupModel> lookups,
        HashSet<string> propertySets,
        HashSet<string> excludedProperties,
        HashSet<string>? explicitProperties,
        RepositoryEntityDesign? design)
    {
        if (properties is null ||
            properties.Count == 0 ||
            properties.Any(static property => property.IsShadowProperty()) ||
            properties.Any(property => excludedProperties.Contains(property.Name)) ||
            explicitProperties is not null &&
            properties.Count == 1 &&
            explicitProperties.Contains(properties[0].Name))
        {
            return;
        }

        var propertySet = string.Join("|", properties.Select(static property => property.Name));
        if (!propertySets.Add(propertySet))
        {
            return;
        }

        var lookupProperties = properties.Select(property => CreateLookupProperty(property,
            design?.Accessors.FirstOrDefault(accessor => accessor.PropertyName == property.Name))).ToArray();
        methodName ??= "By" + string.Join("And", properties.Select(static property => property.Name));
        lookups.Add(new LookupModel(methodName, isUnique, lookupProperties));
    }

    private static LookupPropertyModel CreateLookupProperty(IProperty property, RepositoryAccessorDesign? accessor)
    {
        var name = ToParameterName(property.Name);
        var inputType = property.ClrType;
        LambdaExpression? mapping = accessor?.InputMapping;
        if (accessor?.ValueConverterInputType is { } provider)
        {
            var converter = property.GetTypeMapping().Converter;
            if (converter is null || converter.ModelClrType != property.ClrType || converter.ProviderClrType != provider)
            {
                throw new InvalidOperationException($"RGD1704: Accessor '{property.DeclaringType.Name}.{property.Name}' requires a configured EF converter with exact model '{property.ClrType}' / provider '{provider}' CLR types. Use an explicit type adapter for lifted or different types.");
            }

            mapping = converter.ConvertFromProviderExpression;
        }

        string? comparison = null;
        if (mapping is not null)
        {
            inputType = mapping.Parameters[0].Type;
            string mapped;
            try { mapped = ProjectionExpressionRenderer.RenderBody(mapping, [name]); }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException($"RGD1704: Accessor '{property.Name}' input mapping cannot be emitted as a self-contained expression. {exception.Message}", exception);
            }

            // EF converters skip nulls; the reverse accessor mapping must do the same for nullable references.
            if (accessor?.ValueConverterInputType is not null && property.IsNullable && !inputType.IsValueType)
            {
                mapped = "(" + name + " == null ? null : " + mapped + ")";
            }

            comparison = "global::System.Object.Equals(entity." + EscapeIdentifier(property.Name) + ", " + mapped + ")";
        }

        var typeName = CSharpTypeName(inputType, property.IsNullable);
        if (accessor?.InputNullability is { } shape)
        {
            if (shape.Type != inputType)
            {
                throw new InvalidOperationException($"RGN0002: Accessor '{property.Name}' input nullability must match its effective input CLR type '{inputType}'.");
            }

            typeName = RepositoryTypeName.Annotated(shape);
        }

        return new LookupPropertyModel(property.Name, name, typeName, comparison, inputType);
    }

    private static ProjectionModel[] CreateProjections(IEntityType entity, RepositoryEntityDesign? design)
    {
        if (design is null)
        {
            return [];
        }

        return design.Projections.Select(projection =>
        {
            if (projection.UseExactMethodName)
            {
                var exactMethodName = projection.MethodName
                                      ?? throw new InvalidOperationException(
                                          $"Configured projection '{projection.ProjectionType.FullName}' requires a method name.");
                return new ProjectionModel(
                    projection.ProjectionType.Namespace ?? string.Empty,
                    projection.ProjectionType.IsPublic ? "public" : "internal",
                    ApplyResultNullability(projection.MethodName ?? "projection", projection.ResultNullability, projection.ProjectionType),
                    exactMethodName,
                    exactMethodName,
                    ProjectionExpressionRenderer.Render(projection.Expression, projection.ResultNullability),
                    CreateProjectionIncludes(entity, projection),
                    RenderNullableProjection(projection.Expression, projection.ResultNullability));
            }

            var suffix = projection.MethodName;
            if (string.IsNullOrWhiteSpace(suffix))
            {
                suffix = projection.ProjectionType.Name.Split('`')[0];
            }

            if (suffix!.EndsWith("Async", StringComparison.Ordinal))
            {
                suffix = suffix[..^"Async".Length];
            }

            if (suffix.StartsWith("As", StringComparison.Ordinal))
            {
                suffix = suffix[2..];
            }

            return new ProjectionModel(
                projection.ProjectionType.Namespace ?? string.Empty,
                projection.ProjectionType.IsPublic ? "public" : "internal",
                ApplyResultNullability(projection.MethodName ?? "projection", projection.ResultNullability, projection.ProjectionType),
                "As" + suffix + "Async",
                "As" + Pluralize(suffix) + "Async",
                ProjectionExpressionRenderer.Render(projection.Expression, projection.ResultNullability),
                CreateProjectionIncludes(entity, projection),
                RenderNullableProjection(projection.Expression, projection.ResultNullability));
        }).ToArray();
    }

    private static string RenderNullableProjection(LambdaExpression projection, RepositoryTypeNullabilityDesign? resultNullability = null)
    {
        if (!projection.ReturnType.IsValueType || Nullable.GetUnderlyingType(projection.ReturnType) is not null)
        {
            return ProjectionExpressionRenderer.Render(projection, resultNullability);
        }

        return ProjectionExpressionRenderer.Render(Expression.Lambda(
            Expression.Convert(projection.Body, typeof(Nullable<>).MakeGenericType(projection.ReturnType)), projection.Parameters), resultNullability);
    }

    private static IncludeModel[] CreateProjectionIncludes(
        IEntityType entity,
        RepositoryProjectionDesign projection)
    {
        var includes = RepositoryIncludeSourceRenderer.Create(entity, projection.Includes).ToList();
        var roots = includes.SelectMany(static include => include.Paths ?? []).Select(static path => path.Path.Split('.')[0]).ToHashSet(StringComparer.Ordinal);

        foreach (var navigationName in projection.AutoIncludeNavigations ? ProjectionNavigationFinder.Find(entity, projection.Expression) : [])
        {
            if (roots.Contains(navigationName))
            {
                continue;
            }

            var parameter = projection.Expression.Parameters[0];
            var navigation = Expression.Lambda(
                Expression.Property(parameter, navigationName),
                parameter);
            includes.Add(RepositoryIncludeSourceRenderer.CreateInferred(entity, navigation));
        }

        return includes.ToArray();
    }

    private sealed class ProjectionNavigationFinder : ExpressionVisitor
    {
        private readonly IEntityType entity;
        private readonly ParameterExpression parameter;
        private readonly HashSet<string> navigationNames = new(StringComparer.Ordinal);

        private ProjectionNavigationFinder(IEntityType entity, ParameterExpression parameter)
        {
            this.entity = entity;
            this.parameter = parameter;
        }

        public static string[] Find(IEntityType entity, LambdaExpression projection)
        {
            var finder = new ProjectionNavigationFinder(entity, projection.Parameters[0]);
            finder.Visit(projection.Body);
            return finder.navigationNames.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (TryGetRootMember(node, out var memberName) &&
                (entity.FindNavigation(memberName) is not null ||
                 entity.FindSkipNavigation(memberName) is not null))
            {
                navigationNames.Add(memberName);
            }

            return base.VisitMember(node);
        }

        private bool TryGetRootMember(MemberExpression member, out string memberName)
        {
            var current = member;
            while (current.Expression is MemberExpression parent)
            {
                current = parent;
            }

            memberName = current.Member.Name;
            return current.Expression == parameter;
        }
    }

    private static ConfiguredQueryModel[] CreateConfiguredQueries(
        IEntityType entity,
        RepositoryEntityDesign? design,
        RepositoryGenerationSettingsModel settings,
        RepositoryDesignModel? fullDesign,
        string contextTypeName, string? providerName, IModel? inMemoryRelationshipMetadata)
    {
        if (design is null)
        {
            return [];
        }

        return design.Queries.Where(query => settings.ContractNamespace is null && !settings.CqrsSelectors ||
                                             (query.Target == RepositoryQueryTarget.Write ? design.WriteEnabled : design.ReadEnabled)).Select(query =>
        {
            var cacheEnabled = settings.GenerateCacheDecorator && query.Cache.Enabled;
            if (cacheEnabled && query.Target == RepositoryQueryTarget.Write)
            {
                throw new InvalidOperationException($"RGD2103: Command-side query '{entity.ClrType.FullName}.{query.MethodName}' cannot enable caching. Loading command state must execute against its selected context, not return a cached detached result.");
            }

            if (cacheEnabled && query.ResultKind == RepositoryQueryResultKind.Stream)
            {
                throw new InvalidOperationException(
                    $"Configured query '{entity.ClrType.FullName}.{query.MethodName}' cannot cache a streamed result.");
            }

            if (query.ResultKind == RepositoryQueryResultKind.CursorPaged && query.Cursor is null)
            {
                throw new InvalidOperationException(
                    $"Configured query '{entity.ClrType.FullName}.{query.MethodName}' requires cursor configuration.");
            }

            if (query.ResultKind == RepositoryQueryResultKind.GroupedList && query.Group is null)
            {
                throw new InvalidOperationException(
                    $"Configured query '{entity.ClrType.FullName}.{query.MethodName}' requires group configuration.");
            }

            var predicates = CreateConfiguredPredicates(query.Predicates, out var parameters);
            var membershipDesigns = query.Memberships.Concat(query.ResultStages.Select(static stage => stage.Membership).OfType<RepositoryMembershipDesign>()).ToArray();
            var memberships = membershipDesigns.Select((membership, index) => CreateMembership(membership, index, parameters, cacheEnabled)).ToArray();
            predicates = predicates.Concat(memberships.Take(query.Memberships.Count)
                .Select(static membership => new ConfiguredPredicateModel(membership.PredicateSource, membership.ApplyName))).ToArray();
            var contextKey = query.ContextKey is null ? null : RenderBoundExpression(query.ContextKey, parameters, skipRow: false);
            var validations = CreateInputValidations(query.Validations, parameters);
            predicates = predicates.Concat(query.ConditionalPredicates.Select(predicate => new ConfiguredPredicateModel(
                RenderBoundExpression(predicate.Predicate, parameters, skipRow: true, lambda: false),
                RenderBoundExpression(predicate.Condition, parameters, skipRow: false)))).ToArray();
            var sorts = query.Sorts.Select(sort => new SortModel(sort.IsDescending,
                sort.KeySelector.Parameters.Count == 1
                    ? ProjectionExpressionRenderer.Render(sort.KeySelector)
                    : RenderBoundExpression(sort.KeySelector, parameters, skipRow: true))).ToArray();
            var dynamicSort = CreateConfiguredDynamicSort(entity, query.DynamicSort);
            if (dynamicSort is not null)
            {
                AddParameter(parameters, dynamicSort.FieldNameParameter, typeof(string));
                AddParameter(parameters, dynamicSort.DescendingParameter, typeof(bool));
            }

            var defaultTracking = settings.ContractNamespace is null && !settings.CqrsSelectors || query.Target == RepositoryQueryTarget.Write || settings.ReadOnlyTrackingBehavior == RepositoryTrackingBehavior.Unspecified
                ? settings.DefaultTrackingBehavior
                : settings.ReadOnlyTrackingBehavior;
            var tracking = query.TrackingBehavior == RepositoryTrackingBehavior.Unspecified
                ? defaultTracking
                : query.TrackingBehavior;
            var source = RepositoryQuerySourceRenderer.Create(entity, query, parameters, tracking, fullDesign);
            ConfiguredRegisteredQueryModel? registeredRoot = null;
            if (query.RegisteredSource is { } registered)
            {
                if (registered.ProviderType.GetCustomAttribute<RepositoryProviderAttribute>() is { } requirement && requirement.ProviderName != providerName)
                {
                    throw new InvalidOperationException($"RGD1302: Query provider '{registered.ProviderType.FullName}' requires '{requirement.ProviderName}', but the design-time context uses '{providerName}'.");
                }

                if (settings.ContractNamespace is null)
                {
                    throw new InvalidOperationException("RGD1303: Registered query providers require closed repositories so infrastructure query roots cannot escape through service selectors.");
                }

                var capability = registered.ProviderType.GetInterfaces().FirstOrDefault(type => type.IsGenericType &&
                                                                                                type.GetGenericTypeDefinition().FullName == "RonSijm.RepoGen.EntityFrameworkCore.IRepositoryQueryRoot`3" &&
                                                                                                CSharpTypeName(type.GetGenericArguments()[0]) == contextTypeName && type.GetGenericArguments()[1] == registered.RequestType && type.GetGenericArguments()[2] == registered.ResultType);
                if (capability is null || !registered.ProviderType.IsVisible || registered.ProviderType.IsAbstract || registered.ProviderType.ContainsGenericParameters)
                {
                    throw new InvalidOperationException($"RGD1303: Query provider '{registered.ProviderType.FullName}' must be a public concrete IRepositoryQueryRoot for the selected context/request/result.");
                }

                if (query.Predicates.Count > 0 || query.Memberships.Count > 0 || query.ConditionalPredicates.Count > 0 || query.Sorts.Count > 0 || query.Includes.Count > 0 || query.LoadProfileNames.Count > 0 ||
                    query.Projection is not null || query.Group is not null || query.Cursor is { OnResults: false } || query.DynamicSort is not null || query.IgnoreQueryFilters || query.IgnoreAutoIncludes ||
                    query.QueryTag is not null || query.SplittingBehavior != RepositoryQuerySplittingBehavior.Unspecified || query.TrackingBehavior != RepositoryTrackingBehavior.Unspecified)
                {
                    throw new InvalidOperationException($"RGD1304: Registered query '{query.MethodName}' owns its root policies. Declare visibility/tracking/loading/tags inside the registered provider, and only result stages in RepoGen.");
                }

                var rootIdentity = registered.ProviderType.AssemblyQualifiedName + ":" + registered.RequestBinding;
                var rootSuffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rootIdentity)))[..12];
                registeredRoot = new(CSharpTypeName(registered.ProviderType), "__repoGen" + EnsureAsyncSuffix(query.MethodName) + "Root" + rootSuffix);
                source = new(registeredRoot.FieldName + ".CreateQuery(context, " + RenderBoundExpression(registered.RequestBinding, parameters, skipRow: false) + ")",
                    [], ["RegisteredProvider"]);
            }

            ValidateVisibilityBypass(query.MethodName, query.BypassedVisibility,
                source is null
                    ? design.VisibilityPolicies.Select(static policy => policy.Name)
                    : (fullDesign?.Entities ?? []).Where(definition => source.EntityTypeNames.Contains(CSharpTypeName(definition.EntityType), StringComparer.Ordinal))
                    .SelectMany(static definition => definition.VisibilityPolicies).Select(static policy => policy.Name));
            var projection = query.Projection;
            var sourceResultType = query.RegisteredSource?.ResultType ?? query.Source?.ResultType ?? query.Group?.ResultType ?? projection?.ProjectionType ?? entity.ClrType;
            var resultType = ValidateResultPipeline(query, sourceResultType);
            if (query.Cursor is { OnResults: true } resultCursor &&
                (resultCursor.SeekPredicate.Parameters[0].Type != resultType || resultCursor.CursorSelector.Parameters[0].Type != resultType))
            {
                throw new InvalidOperationException($"Query '{query.MethodName}' changes its result type after configuring CursorPaged. Configure the cursor on the final result shape.");
            }

            if (query.RequirePagingOrder && query.ResultKind is RepositoryQueryResultKind.Paged or RepositoryQueryResultKind.Slice or RepositoryQueryResultKind.CursorPaged)
            {
                if (!HasGuaranteedResultOrder(query, design, source is not null))
                {
                    throw new InvalidOperationException($"RGD1501: Query '{query.MethodName}' requires an explicit paging OrderBy (and a unique tie-breaker where necessary). Declare result ordering after replacement roots/grouping/distinct.");
                }
            }

            if (query.Dictionary is { DuplicateKeys: not RepositoryDuplicateKeyBehavior.Throw } &&
                !HasGuaranteedResultOrder(query, design, source is not null))
            {
                throw new InvalidOperationException($"RGD2502: Query '{query.MethodName}' dictionary {query.Dictionary.DuplicateKeys} requires ordering guaranteed on every branch. Declare an unconditional OrderBy after replacement roots/grouping/distinct and include a unique tie-breaker where necessary.");
            }

            var usePipeline = query.ResultInputType is not null ||
                              query.Group is not null && query.ResultKind != RepositoryQueryResultKind.GroupedList;
            var resultNullability = query.ResultNullability ?? (query.Dictionary is null && query.Scalar is null &&
                                                                query.ResultKind is not (RepositoryQueryResultKind.Count or RepositoryQueryResultKind.Exists) && resultType == projection?.ProjectionType
                ? projection.ResultNullability
                : null);
            var pipeline = usePipeline
                ? CreateResultPipeline(query, resultType, parameters, resultNullability,
                    membershipDesigns.Zip(memberships).ToDictionary(static pair => pair.First, static pair => pair.Second))
                : null;
            var projectionExpression = projection is null
                ? "static entity => entity"
                : ProjectionExpressionRenderer.Render(projection.Expression, projection.Expression.ReturnType == resultNullability?.Type ? resultNullability : projection.ResultNullability);
            if (!usePipeline && projection is not null && query.ResultKind is RepositoryQueryResultKind.SingleOrDefault or RepositoryQueryResultKind.FirstOrDefault)
            {
                projectionExpression = RenderNullableProjection(projection.Expression, projection.ResultNullability);
            }

            var projectionBody = projection is null
                ? "entity"
                : ProjectionExpressionRenderer.RenderBody(projection.Expression, ["entity"], resultNullability: projection.Expression.ReturnType == resultNullability?.Type ? resultNullability : projection.ResultNullability);
            var outputType = query.Dictionary?.ValueSelector.ReturnType ?? query.Scalar?.ReturnType ??
                (query.ResultKind == RepositoryQueryResultKind.Count ? typeof(int) : query.ResultKind == RepositoryQueryResultKind.Exists ? typeof(bool) : resultType);
            var outputTypeName = ApplyResultNullability(query.MethodName, resultNullability, outputType);
            var keyTypeName = ApplyKeyNullability(query);
            if (!usePipeline && resultNullability is not null && query.ResultKind is not (RepositoryQueryResultKind.Count or RepositoryQueryResultKind.Exists or RepositoryQueryResultKind.GroupedList))
            {
                var projectedType = query.ResultKind is RepositoryQueryResultKind.SingleOrDefault or RepositoryQueryResultKind.FirstOrDefault &&
                                    resultType.IsValueType && Nullable.GetUnderlyingType(resultType) is null
                    ? outputTypeName + "?"
                    : outputTypeName;
                projectionExpression = "entity => (" + projectedType + ")(" + projectionBody + ")";
            }

            var includes = CreateConfiguredQueryIncludes(entity, query, parameters);
            var allIncludes = source is null ? includes.Concat(CreateIncludes(entity, design)).ToArray() : includes;
            RepositoryIncludeSourceRenderer.Validate(allIncludes, query.RequireExplicitLoadStrategy, query.SplittingBehavior);
            var dependencies = new HashSet<string>(
                query.Cache.DependentEntityTypes.Select(static type => CSharpTypeName(type)),
                StringComparer.Ordinal);
            if (source is not null)
            {
                dependencies.UnionWith(source.EntityTypeNames);
            }

            dependencies.UnionWith(allIncludes.SelectMany(static include => include.DependentEntityTypeNames ?? []));
            if (projection is not null)
            {
                dependencies.UnionWith(RepositoryQuerySourceRenderer.NavigationDependencies(entity.Model, projection.Expression)
                    .Select(static type => CSharpTypeName(type)));
            }

            if (query.Group is not null)
            {
                dependencies.UnionWith(new[] { query.Group.KeySelector, query.Group.Projection }
                    .SelectMany(expression => RepositoryQuerySourceRenderer.NavigationDependencies(entity.Model, expression))
                    .Select(static type => CSharpTypeName(type)));
            }

            foreach (var stage in query.ResultStages)
            {
                if (stage.Expression is not null)
                {
                    foreach (var type in RepositoryQuerySourceRenderer.NavigationDependencies(entity.Model, stage.Expression))
                    {
                        dependencies.Add(CSharpTypeName(type));
                    }
                }

                if (stage.Projection is not null)
                {
                    foreach (var type in RepositoryQuerySourceRenderer.NavigationDependencies(entity.Model, stage.Projection))
                    {
                        dependencies.Add(CSharpTypeName(type));
                    }
                }
            }

            foreach (var membership in membershipDesigns)
            {
                dependencies.UnionWith(RepositoryQuerySourceRenderer.NavigationDependencies(entity.Model, membership.KeySelector).Select(static type => CSharpTypeName(type)));
            }

            // Predicates, sorts, dictionary/scalar/cursor terminals and source policies can traverse
            // related rows even when the final projection contains only owner columns.
            var dependencyExpressions = RepositoryExpressionInventory.Query(query);
            if (source is null)
            {
                dependencyExpressions = dependencyExpressions.Concat(RepositoryExpressionInventory.Root(design, query));
            }
            else if (registeredRoot is null)
            {
                dependencyExpressions = dependencyExpressions.Concat(RepositoryExpressionInventory.SourcePolicies(fullDesign, query));
            }

            dependencies.UnionWith(dependencyExpressions.SelectMany(expression => RepositoryQuerySourceRenderer.NavigationDependencies(entity.Model, expression))
                .Select(static type => CSharpTypeName(type)));
            if (!query.IgnoreQueryFilters && registeredRoot is null)
            {
                dependencies.UnionWith(RepositoryQuerySourceRenderer.QueryFilterDependencies(entity.Model,
                    dependencies.Append(CSharpTypeName(entity.ClrType))).Select(static type => CSharpTypeName(type)));
            }

            var cursor = CreateCursor(query.Cursor, parameters);
            var cursorValidations = query.CursorValidations.Select(validation =>
            {
                var names = new List<string> { "cursorPage.Cursor!" };
                if (validation.Predicate.Parameters.Count == 2)
                {
                    var request = validation.Predicate.Parameters[1];
                    var name = ToParameterName(request.Name ?? "value");
                    ClosedRepositorySourceRenderer.ValidateParameterName(name);
                    AddParameter(parameters, name, request.Type);
                    names.Add(name);
                }

                return new ConfiguredInputValidationModel(
                    ProjectionExpressionRenderer.RenderBody(validation.Predicate, names),
                    System.Text.Json.JsonSerializer.Serialize(validation.Message));
            }).ToArray();
            var group = query.Group is null
                ? null
                : new ConfiguredGroupedQueryModel(
                    query.Group.KeySelector.Parameters.Count == 1 ? ProjectionExpressionRenderer.Render(query.Group.KeySelector) : RenderBoundExpression(query.Group.KeySelector, parameters, skipRow: true),
                    !usePipeline && resultNullability is not null
                        ? "group => " + AnnotateResultExpression(resultNullability, outputTypeName, RenderBoundExpression(query.Group.Projection, parameters, skipRow: true, lambda: false, rowName: "group", resultNullability: resultNullability))
                        : query.Group.Projection.Parameters.Count == 1
                            ? ProjectionExpressionRenderer.Render(query.Group.Projection,
                                query.Group.ResultType == resultNullability?.Type ? resultNullability : null)
                            : RenderBoundExpression(query.Group.Projection, parameters, skipRow: true, rowName: "group",
                                resultNullability: query.Group.ResultType == resultNullability?.Type ? resultNullability : null));

            // Terminal clauses can introduce the only request parameter. Register it before
            // applying annotations and capturing API input types, exactly like result stages.
            var dictionary = query.Dictionary is null
                ? null
                : new ConfiguredDictionaryModel(
                    keyTypeName!, outputTypeName,
                    AnnotateResultExpression(query.DictionaryKeyNullability, keyTypeName!, RenderBoundExpression(query.Dictionary.KeySelector,
                        parameters, skipRow: true, lambda: false, resultNullability: query.DictionaryKeyNullability)),
                    AnnotateResultExpression(resultNullability, outputTypeName, RenderBoundExpression(query.Dictionary.ValueSelector,
                        parameters, skipRow: true, lambda: false, resultNullability: resultNullability)),
                    query.Dictionary.DuplicateKeys);
            var scalar = query.Scalar is null
                ? null
                : new ConfiguredScalarModel(outputTypeName,
                    resultNullability is null
                        ? query.Scalar.Selector.Parameters.Count == 1
                            ? ProjectionExpressionRenderer.Render(query.Scalar.Selector)
                            : RenderBoundExpression(query.Scalar.Selector, parameters, skipRow: true)
                        : "entity => " + AnnotateResultExpression(resultNullability, outputTypeName,
                            RenderBoundExpression(query.Scalar.Selector, parameters, skipRow: true, lambda: false, resultNullability: resultNullability)));
            ApplyInputNullability(query.MethodName, query.InputNullability, parameters);

            var inMemoryQuery = CreateInMemoryQuery(entity, design, query, settings, allIncludes, fullDesign, parameters, inMemoryRelationshipMetadata, out var translations);
            var configuredQuery = new ConfiguredQueryModel(
                settings.GeneratedNamespace ?? resultType.Namespace ?? entity.ClrType.Namespace ?? string.Empty,
                entity.ClrType.IsPublic && resultType.IsPublic ? "public" : "internal",
                EnsureAsyncSuffix(query.MethodName),
                query.Dictionary is null && query.Scalar is null && resultNullability is not null ? outputTypeName : CSharpTypeName(resultType),
                query.ResultKind,
                predicates,
                parameters,
                includes,
                sorts,
                dynamicSort,
                projectionExpression,
                projectionBody,
                tracking,
                query.SplittingBehavior,
                query.IgnoreQueryFilters,
                query.QueryTag,
                new ConfiguredQueryCacheModel(
                    cacheEnabled,
                    query.Cache.AbsoluteExpirationRelativeToNow?.Ticks,
                    query.Cache.SlidingExpiration?.Ticks,
                    dependencies.OrderBy(static name => name, StringComparer.Ordinal).ToArray()),
                cursor,
                group,
                pipeline,
                dictionary,
                scalar,
                query.UseDefaultSort,
                source,
                (source is null ? design.LoadProfileNames : []).Concat(query.LoadProfileNames).Concat(query.Projection?.LoadProfileNames ?? [])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                query.BypassedVisibility.ToArray(), contextKey, validations, registeredRoot, query.PageConsistency,
                resultNullability is not null && query.Dictionary is null && query.Scalar is null && query.ResultKind is not (RepositoryQueryResultKind.Count or RepositoryQueryResultKind.Exists),
                parameters.Select(static parameter => parameter.RuntimeType!).Append(outputType)
                    .Concat(query.Dictionary is null ? [] : new[] { query.Dictionary.KeySelector.ReturnType })
                    .Concat(query.Cursor is null ? [] : new[] { query.Cursor.CursorType }).Distinct().ToArray(),
                settings.GeneratedNamespace, memberships.Length == 0 ? null : memberships, query.Target,
                ResolveCommandTimeout(settings, design.CommandTimeout, query.CommandTimeout, query.MethodName),
                cursorValidations.Length == 0 ? null : cursorValidations,
                inMemoryQuery, query.IgnoreAutoIncludes);
            if (inMemoryQuery?.Supported == true && translations?.Receipts.Count > 0)
            {
                var rendering = CreateInMemoryTranslatedQuery(design, query, configuredQuery, translations, resultType, resultNullability);
                return configuredQuery with { InMemoryQuery = inMemoryQuery with { Translations = translations.Receipts, Rendering = rendering } };
            }
            return configuredQuery;
        }).ToArray();
    }

    private static ConfiguredCursorModel? CreateCursor(RepositoryCursorDesign? cursor, List<FilterParameterModel> parameters,
        InMemoryTranslationExpressions? translations = null)
    {
        if (cursor is null)
        {
            return null;
        }

        var seekNames = new List<string> { "entity", "cursor" };
        string? requestName = null;
        if (cursor.SeekPredicate.Parameters.Count == 3)
        {
            var request = cursor.SeekPredicate.Parameters[2];
            requestName = ToParameterName(request.Name ?? "value");
            ClosedRepositorySourceRenderer.ValidateParameterName(requestName);
            AddParameter(parameters, requestName, request.Type);
            seekNames.Add(requestName);
        }

        if (cursor.CursorSelector.Parameters.Count == 2)
        {
            requestName ??= ToParameterName(cursor.CursorSelector.Parameters[1].Name ?? "value");
        }

        return new ConfiguredCursorModel(CSharpTypeName(cursor.CursorType),
            ProjectionExpressionRenderer.RenderBody(translations?.Expand(cursor.SeekPredicate) ?? cursor.SeekPredicate, seekNames),
            cursor.CursorSelector.Parameters.Count == 1
                ? ProjectionExpressionRenderer.Render(translations?.Expand(cursor.CursorSelector) ?? cursor.CursorSelector)
                : RenderBoundExpression(translations?.Expand(cursor.CursorSelector) ?? cursor.CursorSelector, parameters, skipRow: true),
            RenderBoundExpression(translations?.Expand(cursor.CursorSelector) ?? cursor.CursorSelector, parameters, skipRow: true, lambda: false),
            cursor.OnResults, requestName);
    }

    private static bool HasGuaranteedResultOrder(RepositoryQueryDesign query, RepositoryEntityDesign design, bool replacementRoot)
    {
        var ordered = !replacementRoot && query.Group is null &&
                      (query.Sorts.Count > 0 || query.DynamicSort is not null || query.UseDefaultSort && design.DefaultSorts.Count > 0);
        foreach (var stage in query.ResultStages)
        {
            if (stage.Kind is RepositoryQueryStageKind.OrderBy or RepositoryQueryStageKind.OrderByDescending && stage.Condition is null)
            {
                ordered = true;
            }

            if (stage.FieldSort is { PreserveOrderWhenUnspecified: false })
            {
                ordered = true;
            }

            if (stage.Kind is RepositoryQueryStageKind.GroupBy or RepositoryQueryStageKind.Distinct)
            {
                ordered = false;
            }
        }

        return ordered;
    }

    private static Type ValidateResultPipeline(RepositoryQueryDesign query, Type sourceType)
    {
        if (query.CursorValidations.Count > 0 && (query.ResultKind != RepositoryQueryResultKind.CursorPaged || query.Cursor is null ||
                                                  query.CursorValidations.Any(validation => validation.Predicate.Parameters[0].Type != query.Cursor.CursorType)))
        {
            throw new InvalidOperationException($"RGD2702: Query '{query.MethodName}' cursor guards require CursorPaged with the same cursor type. Configure guards on the final cursor operation.");
        }

        if (query.ResultInputType is not null && query.ResultInputType != sourceType)
        {
            throw new InvalidOperationException($"Query '{query.MethodName}' Results<{query.ResultInputType.Name}> does not match its projection/group result '{sourceType.FullName}'.");
        }

        if (query.ResultInputType is not null && query.ResultKind == RepositoryQueryResultKind.CursorPaged && query.Cursor?.OnResults != true)
        {
            throw new InvalidOperationException($"Query '{query.MethodName}' cannot apply result stages to an entity cursor. Use Paged()/Slice() or configure CursorPaged() without Results().");
        }

        if (query.PageConsistency != RepositoryPageConsistency.BestEffort && query.ResultKind != RepositoryQueryResultKind.Paged)
        {
            throw new InvalidOperationException($"Query '{query.MethodName}' page consistency is only supported by counted Paged operations.");
        }

        var current = sourceType;
        var ordered = false;
        var conditionalOrdering = false;
        foreach (var stage in query.ResultStages)
        {
            if (stage.InputType != current)
            {
                throw new InvalidOperationException($"Query '{query.MethodName}' has an incompatible '{stage.Kind}' result stage.");
            }

            if (stage.Kind is RepositoryQueryStageKind.ThenBy or RepositoryQueryStageKind.ThenByDescending && !ordered)
            {
                if (conditionalOrdering || stage.Condition is not null)
                {
                    throw new InvalidOperationException($"RGD2401: Query '{query.MethodName}' ThenBy requires adjacent result ordering guaranteed on every branch. Configure an unconditional OrderBy fallback before conditional ordering.");
                }

                throw new InvalidOperationException($"Query '{query.MethodName}' ThenBy requires an immediately preceding OrderBy/ThenBy result stage. Apply other clauses before sorting, or start a new OrderBy.");
            }

            var sorting = stage.Kind is RepositoryQueryStageKind.OrderBy or RepositoryQueryStageKind.OrderByDescending or
                RepositoryQueryStageKind.ThenBy or RepositoryQueryStageKind.ThenByDescending or RepositoryQueryStageKind.OrderByField;
            var conditionalSort = stage.Condition is not null || stage.FieldSort is { PreserveOrderWhenUnspecified: true };
            conditionalOrdering = sorting && (conditionalOrdering || conditionalSort);
            ordered = sorting && (!conditionalSort || ordered);
            current = stage.OutputType;
        }

        if (query.Dictionary is not null && Nullable.GetUnderlyingType(query.Dictionary.KeySelector.ReturnType) is not null)
        {
            throw new InvalidOperationException($"Query '{query.MethodName}' dictionary keys must be non-nullable.");
        }

        if (query.Dictionary is not null && (query.Dictionary.KeySelector.Parameters[0].Type != current ||
                                             query.Dictionary.ValueSelector.Parameters[0].Type != current) ||
            query.Scalar is not null && query.Scalar.Selector.Parameters[0].Type != current)
        {
            throw new InvalidOperationException($"Query '{query.MethodName}' terminal selector does not match the final result stage. Configure the terminal after the last Select/GroupBy.");
        }

        if (ProjectionExpressionRenderer.IsAnonymousType(current) && query.ResultKind is RepositoryQueryResultKind.List or RepositoryQueryResultKind.GroupedList or
                RepositoryQueryResultKind.Single or RepositoryQueryResultKind.SingleOrDefault or RepositoryQueryResultKind.FirstOrDefault or
                RepositoryQueryResultKind.Paged or RepositoryQueryResultKind.Slice or RepositoryQueryResultKind.Stream)
        {
            throw new InvalidOperationException($"Query '{query.MethodName}' has an anonymous final result. Select a named DTO or scalar before choosing its terminal.");
        }

        return current;
    }

    private static ConfiguredResultPipelineModel CreateResultPipeline(
        RepositoryQueryDesign query, Type resultType, List<FilterParameterModel> parameters, RepositoryTypeNullabilityDesign? resultNullability,
        Dictionary<RepositoryMembershipDesign, ConfiguredMembershipModel> memberships, InMemoryTranslationExpressions? translations = null)
    {
        var stages = query.ResultStages.Select((stage, index) =>
        {
            if (stage.FieldSort is { } fieldSort)
            {
                var configured = new ConfiguredFieldSortModel("__repoGenFieldSort" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ToParameterName(fieldSort.FieldName.Parameters[0].Name ?? "value"),
                    RenderBoundExpression(translations?.Expand(fieldSort.FieldName) ?? fieldSort.FieldName, parameters, skipRow: false),
                    RenderBoundExpression(translations?.Expand(fieldSort.Descending) ?? fieldSort.Descending, parameters, skipRow: false),
                    fieldSort.Fields.Select(field => new ConfiguredSortFieldModel(field.Name, ProjectionExpressionRenderer.Render(translations?.Expand(field.KeySelector) ?? field.KeySelector))).ToArray(),
                    fieldSort.PreserveOrderWhenUnspecified, fieldSort.DefaultField, fieldSort.DefaultDescending);
                return new ConfiguredQueryStageModel(stage.Kind, null, null,
                    ProjectionExpressionRenderer.IsAnonymousType(stage.InputType) ? "<anonymous>" : CSharpTypeName(stage.InputType), FieldSort: configured);
            }

            if (stage.Membership is { } membership)
            {
                var model = memberships[membership];
                return new ConfiguredQueryStageModel(stage.Kind, "entity => " + model.PredicateSource, model.ApplyName,
                    ProjectionExpressionRenderer.IsAnonymousType(stage.InputType) ? "<anonymous>" : CSharpTypeName(stage.InputType));
            }

            var countStage = stage.Kind is RepositoryQueryStageKind.Skip or RepositoryQueryStageKind.Take;
            var expression = stage.Expression is null
                ? stage.Count?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : RenderBoundExpression(translations?.Expand(stage.Expression) ?? stage.Expression, parameters, skipRow: !countStage,
                    resultNullability: stage.Kind == RepositoryQueryStageKind.Select && stage.Expression.ReturnType == resultNullability?.Type ? resultNullability : null);
            return new ConfiguredQueryStageModel(stage.Kind, expression,
                stage.Condition is null ? null : RenderBoundExpression(translations?.Expand(stage.Condition) ?? stage.Condition, parameters, skipRow: false),
                ProjectionExpressionRenderer.IsAnonymousType(stage.InputType) ? "<anonymous>" : CSharpTypeName(stage.InputType),
                stage.Projection is null ? null
                : stage.Projection.Parameters.Count == 1 ? ProjectionExpressionRenderer.Render(translations?.Expand(stage.Projection) ?? stage.Projection,
                    stage.Projection.ReturnType == resultNullability?.Type ? resultNullability : null)
                : RenderBoundExpression(translations?.Expand(stage.Projection) ?? stage.Projection, parameters, skipRow: true, rowName: "group",
                    resultNullability: stage.Projection.ReturnType == resultNullability?.Type ? resultNullability : null));
        }).ToArray();
        return new ConfiguredResultPipelineModel(stages, resultType.IsValueType && Nullable.GetUnderlyingType(resultType) is null);
    }

    private static ConfiguredMembershipModel CreateMembership(RepositoryMembershipDesign membership, int index, List<FilterParameterModel> parameters, bool cacheEnabled,
        InMemoryTranslationExpressions? translations = null)
    {
        var element = membership.KeySelector.ReturnType;
        if (ProjectionExpressionRenderer.IsAnonymousType(element) || element.ContainsGenericParameters || typeof(Delegate).IsAssignableFrom(element))
        {
            throw new InvalidOperationException("RGD1901: Membership keys must have a concrete named CLR type; anonymous keys and delegates are unsupported.");
        }

        var valuesBody = membership.Values.Body;
        while (valuesBody is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
        {
            valuesBody = convert.Operand;
        }

        if (typeof(IQueryable).IsAssignableFrom(valuesBody.Type) || valuesBody.Type == typeof(string))
        {
            throw new InvalidOperationException("RGD1901: Membership values must be an in-memory collection, not IQueryable or a string.");
        }

        if (cacheEnabled && !typeof(System.Collections.Generic.ICollection<>).MakeGenericType(element).IsAssignableFrom(valuesBody.Type) &&
            !typeof(System.Collections.Generic.IReadOnlyCollection<>).MakeGenericType(element).IsAssignableFrom(valuesBody.Type))
        {
            throw new InvalidOperationException("RGD1903: Cached membership queries require materialized array/ICollection/IReadOnlyCollection values. Cache-key serialization must not re-enumerate a one-shot IEnumerable. Materialize the request before calling the repository or disable caching.");
        }

        var local = "__repoGenMembership" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var values = RenderBoundExpression(translations?.Expand(membership.Values) ?? membership.Values, parameters, skipRow: false);
        var key = ProjectionExpressionRenderer.RenderBody(translations?.Expand(membership.KeySelector) ?? membership.KeySelector, ["entity"]);
        var predicate = (membership.Negated ? "!" : "") + "global::System.Linq.Enumerable.Contains(" + local + "Values, " + key + ")";
        var typeName = CSharpTypeName(element) + (!element.IsValueType ? "?" : "");
        return new(local, typeName, ToParameterName(membership.Values.Parameters[0].Name ?? "value1").TrimStart('@'), values, predicate, membership.Negated, membership.Options);
    }

    internal static string RenderBoundExpression(LambdaExpression expression, List<FilterParameterModel> parameters, bool skipRow, bool lambda = true, string rowName = "entity",
        IReadOnlyDictionary<ParameterExpression, string>? substitutions = null, RepositoryTypeNullabilityDesign? resultNullability = null)
    {
        var names = expression.Parameters.Select((parameter, index) => skipRow && index == 0
            ? rowName
            : ToParameterName(parameter.Name ?? "value" + (index + 1))).ToArray();
        for (var index = skipRow ? 1 : 0; index < expression.Parameters.Count; index++)
        {
            ClosedRepositorySourceRenderer.ValidateParameterName(names[index]);
            AddParameter(parameters, names[index], expression.Parameters[index].Type);
        }

        var body = ProjectionExpressionRenderer.RenderBody(expression, names, substitutions, resultNullability);
        return skipRow && lambda ? rowName + " => " + body : body;
    }

    private static ConfiguredCommandModel[] CreateConfiguredCommands(IEntityType entity, RepositoryEntityDesign? design, RepositoryGenerationSettingsModel settings, string? providerName,
        RepositoryDesignModel? modelDesign, string[] protectedWrites, IModel? inMemoryRelationshipMetadata)
    {
        if (design is null)
        {
            return [];
        }

        return design.Commands.Select(command =>
        {
            ValidateVisibilityBypass(command.MethodName, command.BypassedVisibility, design.VisibilityPolicies.Select(static policy => policy.Name));
            var savedResultKeys = ValidateSavedCommandResult(entity, command);
            var tracked = command.Kind is RepositoryCommandKind.Create or RepositoryCommandKind.Patch or RepositoryCommandKind.TrackedDelete;
            var atomic = command.Kind is RepositoryCommandKind.InsertIfAbsent or RepositoryCommandKind.InsertOrUpdate;
            var returning = command.ReturnsMany;
            if (command.Batch is not null && returning)
            {
                throw new InvalidOperationException("RGD3001: Batch saved row projections use Returns, not native ReturnsMany.");
            }
            if (returning)
            {
                if (settings.ReturningWriterTypeName is null)
                {
                    throw new InvalidOperationException($"RGD1305: Returning command '{command.MethodName}' requires UseReturningWrites<TWriter>.");
                }

                if (command.Kind is not RepositoryCommandKind.Update and not RepositoryCommandKind.Delete || command.ResultProjection is null ||
                    command.CreateMapping is not null || command.SaveChanges is not null || command.ReloadAfterSave || command.WithOutcome ||
                    command.Kind == RepositoryCommandKind.Delete && command.Updates.Count != 0)
                {
                    throw new InvalidOperationException($"RGD1306: ReturnsMany on '{command.MethodName}' requires immediate Update/Delete, a scalar result mapping, and no Create/Save/Stage/ReloadAfterSave/WithOutcome configuration.");
                }

                ValidateReturningMapping(entity, command.ResultProjection, providerName);
            }

            if (atomic && settings.AtomicWriterTypeName is null)
            {
                throw new InvalidOperationException($"Atomic command '{command.MethodName}' requires UseAtomicWrites<TWriter>.");
            }

            if (atomic && (command.SaveChanges is not null || command.ReloadAfterSave))
            {
                throw new InvalidOperationException("Atomic commands execute inside their operation transaction; do not choose Save/Stage/ReloadAfterSave.");
            }

            if (command.Kind == RepositoryCommandKind.Unspecified)
            {
                throw new InvalidOperationException(
                    $"Configured command '{command.MethodName}' must end in Delete() or Update(...).");
            }

            if (command.Kind is RepositoryCommandKind.Update or RepositoryCommandKind.Patch && command.Updates.Count == 0 && command.Collections.Count == 0 && command.Batch is null)
            {
                throw new InvalidOperationException(
                    $"Configured update command '{command.MethodName}' must set at least one property.");
            }

            var predicates = CreateConfiguredPredicates(command.Predicates, out var parameters);
            var contextKey = command.ContextKey is null ? null : RenderBoundExpression(command.ContextKey, parameters, skipRow: false);
            var validations = CreateInputValidations(command.Validations, parameters);
            if (command.Kind != RepositoryCommandKind.Create)
            {
                predicates = CreateVisibilityPolicies(design).Where(policy => !command.BypassedVisibility.Contains(policy.Name, StringComparer.Ordinal))
                    .Select(static policy => new ConfiguredPredicateModel(policy.PredicateSource)).Concat(predicates).ToArray();
            }
            else if (!command.ResultFromSaved && command.BypassedVisibility.Count != 0)
            {
                throw new InvalidOperationException("Create commands do not query existing rows and cannot bypass visibility policies.");
            }

            if (tracked && command.SaveChanges is null)
            {
                throw new InvalidOperationException($"Tracked command '{command.MethodName}' must explicitly choose Save() or Stage().");
            }

            if (command.SaveChanges == false && (command.ResultProjection is not null || command.ReloadAfterSave))
            {
                throw new InvalidOperationException($"Staged command '{command.MethodName}' cannot return saved values or reload. Configure a saved command instead.");
            }

            if (!tracked && !atomic && !returning && (command.CreateMapping is not null || command.ResultProjection is not null || command.SaveChanges is not null || command.ReloadAfterSave))
            {
                throw new InvalidOperationException($"Command '{command.MethodName}' uses tracked-only configuration on a set-based operation.");
            }

            if (command.Kind == RepositoryCommandKind.TrackedDelete && command.ReloadAfterSave)
            {
                throw new InvalidOperationException("Deleted rows cannot be reloaded.");
            }

            var allowed = command.AllowedWrites.Select(property => CommandProperty(entity, property).Name).ToHashSet(StringComparer.Ordinal);
            var batch = CreateConfiguredBatch(entity, command, settings, parameters, allowed, protectedWrites);
            var createMany = CreateConfiguredCreateMany(entity, command, settings, parameters);
            string? createSource = null;
            string[] insertColumns = [];
            string[] conflictColumns = [];
            if (command.Kind == RepositoryCommandKind.Create || atomic)
            {
                if (!atomic && (command.Predicates.Count > 0 || command.ExpectedVersions.Count > 0 || command.Updates.Count > 0))
                {
                    throw new InvalidOperationException("Create mappings cannot configure row predicates, expected existing versions or patch setters.");
                }

                var createMapping = command.CreateMany?.Mapping ?? command.CreateMapping;
                if (createMapping?.Body is not MemberInitExpression { NewExpression.Arguments.Count: 0 } creation || creation.Bindings.Count == 0)
                {
                    throw new InvalidOperationException("Create() requires an explicit parameterless new entity initializer with mapped scalar assignments.");
                }

                foreach (var binding in creation.Bindings)
                {
                    if (binding is not MemberAssignment || entity.FindProperty(binding.Member.Name) is not { } property)
                    {
                        throw new InvalidOperationException($"Create assignment '{binding.Member.Name}' must be a mapped scalar. Child graph/reconciliation mappings require an explicit adapter.");
                    }

                    ValidateCommandWrite(property, command, allowed, protectedWrites);
                }

                createSource = command.CreateMany is null ? RenderBoundExpression(createMapping, parameters, skipRow: false) : null;
                insertColumns = creation.Bindings.Select(static binding => binding.Member.Name).ToArray();
                if (atomic)
                {
                    if (command.Kind == RepositoryCommandKind.InsertIfAbsent && (command.Predicates.Count > 0 || command.ExpectedVersions.Count > 0 || command.Updates.Count > 0))
                    {
                        throw new InvalidOperationException("InsertIfAbsent cannot configure existing-row predicates or setters; use InsertOrUpdate for conditional updates.");
                    }

                    if (command.Kind == RepositoryCommandKind.InsertOrUpdate && command.Updates.Count == 0)
                    {
                        throw new InvalidOperationException("InsertOrUpdate must declare its allowed update setters.");
                    }

                    if (command.ConflictKey is null)
                    {
                        throw new InvalidOperationException("An atomic conflict target is required.");
                    }

                    var keys = command.ConflictKey.Body is NewExpression composite ? composite.Arguments : [command.ConflictKey.Body];
                    conflictColumns = keys.Select(key => CommandProperty(entity, Expression.Lambda(key, command.ConflictKey.Parameters)).Name).ToArray();
                    if (conflictColumns.Length == 0 || conflictColumns.Distinct(StringComparer.Ordinal).Count() != conflictColumns.Length ||
                        conflictColumns.Any(name => !insertColumns.Contains(name, StringComparer.Ordinal) || entity.FindProperty(name)!.IsNullable) ||
                        !entity.GetKeys().Any(key => key.Properties.Select(static property => property.Name).SequenceEqual(conflictColumns)) &&
                        !entity.GetIndexes().Any(index => index.IsUnique && index.GetFilter() is null && index.Properties.Select(static property => property.Name).SequenceEqual(conflictColumns)))
                    {
                        throw new InvalidOperationException("The conflict target must be supplied non-null properties of a declared key or unfiltered unique index.");
                    }

                    if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.HasSharedClrType || entity.IsOwned() ||
                        entity.GetProperties().Any(static property => property.PropertyInfo?.SetMethod is null))
                    {
                        throw new InvalidOperationException("Atomic mappings require one ordinary entity with settable CLR scalar properties; inheritance, shadow-only and graph mappings are unsupported.");
                    }
                }
            }

            var updates = command.Updates.Select(update =>
            {
                var parameterNames = update.Value.Parameters
                    .Skip(1)
                    .Select((parameter, index) => ToParameterName(parameter.Name ?? "value" + (index + 1)))
                    .ToArray();
                var expressionNames = new string[parameterNames.Length + 1];
                expressionNames[0] = "entity";
                parameterNames.CopyTo(expressionNames, 1);
                foreach (var parameter in update.Value.Parameters.Skip(1).Select((value, index) => new
                {
                    Name = parameterNames[index],
                    Type = value.Type
                }))
                {
                    AddParameter(parameters, parameter.Name, parameter.Type);
                }

                var property = tracked || atomic || returning || protectedWrites.Length > 0 ? CommandProperty(entity, update.Property) : null;
                if (atomic && property is not null && (property.IsKey() || conflictColumns.Contains(property.Name, StringComparer.Ordinal)))
                {
                    throw new InvalidOperationException("Atomic updates cannot change a key or declared conflict target.");
                }

                if (property is not null)
                {
                    ValidateCommandWrite(property, command, allowed, protectedWrites);
                }

                if (returning && (property!.ValueGenerated != ValueGenerated.Never || property.GetAfterSaveBehavior() != PropertySaveBehavior.Save))
                {
                    throw new InvalidOperationException($"RGD1306: Returning updates cannot assign generated column '{property.Name}'.");
                }

                if (update.Condition is not null && command.Kind != RepositoryCommandKind.Patch)
                {
                    throw new InvalidOperationException("Conditional assignments are supported by tracked Patch() commands only.");
                }

                if (returning && update.Value.ReturnType != property!.ClrType)
                {
                    throw new InvalidOperationException("RGD1306: Returning setters must use the mapped scalar CLR type without widening or boxing.");
                }

                var sourceValue = returning ? ReturningValue(property!, update.Value) : update.Value;
                return new ConfiguredUpdateModel(
                    ProjectionExpressionRenderer.Render(update.Property),
                    "entity => " + ProjectionExpressionRenderer.RenderBody(update.Value, expressionNames),
                    property?.Name, ProjectionExpressionRenderer.RenderBody(sourceValue, expressionNames),
                    update.Condition is null ? null : RenderBoundExpression(update.Condition, parameters, skipRow: false),
                    returning ? CSharpTypeName(sourceValue.ReturnType) + (sourceValue.ReturnType.IsValueType ? "" : "?") : null);
            }).ToArray();
            if (updates.Where(static update => update.PropertyName is not null).GroupBy(static update => update.PropertyName, StringComparer.Ordinal).Any(static group => group.Count() > 1))
            {
                throw new InvalidOperationException($"Command '{command.MethodName}' assigns the same property more than once.");
            }

            if (returning)
            {
                // EF deduplicates identical SQL projections. Emit shared values once and bind all their assignments to that alias.
                var sources = entity.FindPrimaryKey()!.Properties.Select((property, index) =>
                    (Body: "entity." + EscapeIdentifier(property.Name), Type: CSharpTypeName(property.ClrType) + (property.ClrType.IsValueType ? "" : "?"), Alias: "__repoGenKey" + index)).ToList();
                for (var index = 0; index < updates.Length; index++)
                {
                    var update = updates[index];
                    var type = update.NativeValueTypeName!;
                    var source = sources.FindIndex(item => item.Body == update.ValueBodySource && item.Type == type);
                    var alias = source < 0 ? "__repoGenValue" + index : sources[source].Alias;
                    updates[index] = update with { NativeValueAlias = alias };
                    if (source < 0)
                    {
                        sources.Add((update.ValueBodySource!, type, alias));
                    }
                }
            }

            var versions = command.ExpectedVersions.Select(version =>
            {
                var property = CommandProperty(entity, version.Property);
                if (!property.IsConcurrencyToken)
                {
                    throw new InvalidOperationException($"Expected version '{property.Name}' is not configured as an EF concurrency token.");
                }

                var usage = new CommandParameterUsage(version.Value.Parameters[0]);
                usage.Visit(version.Value.Body);
                if (usage.Used)
                {
                    throw new InvalidOperationException("Expected versions must come from input, not the newly fetched entity.");
                }

                if (command.Kind == RepositoryCommandKind.Create)
                {
                    throw new InvalidOperationException("Create commands cannot expect an existing version.");
                }

                var value = RenderBoundExpression(version.Value, parameters, skipRow: true, lambda: false);
                if (!tracked)
                {
                    predicates = predicates.Append(new ConfiguredPredicateModel("entity." + EscapeIdentifier(property.Name) + " == " + value)).ToArray();
                }

                return new ConfiguredVersionModel(property.Name, value);
            }).ToArray();
            if (versions.GroupBy(static version => version.PropertyName, StringComparer.Ordinal).Any(static group => group.Count() > 1))
            {
                throw new InvalidOperationException("An expected version may only be configured once per token.");
            }

            var collections = CreateConfiguredCollections(entity, command, settings, modelDesign, parameters);
            var resultSource = command.ResultProjection is null ? null : RenderBoundExpression(command.ResultProjection, parameters, skipRow: true, lambda: false, resultNullability: command.ResultNullability);
            if (command.ResultNullability is not null && command.ResultProjection is null)
            {
                throw new InvalidOperationException($"RGN0004: Command '{command.MethodName}' result nullability requires a configured Returns value.");
            }

            var resultTypeName = command.ResultProjection is null ? null : ApplyResultNullability(command.MethodName, command.ResultNullability, command.ResultProjection.ReturnType);
            ApplyInputNullability(command.MethodName, command.InputNullability, parameters);
            var inMemoryCommand = CreateInMemoryCommand(entity, design, command, settings, inMemoryRelationshipMetadata, modelDesign, parameters, out var translations);
            var configuredCommand = new ConfiguredCommandModel(
                EnsureAsyncSuffix(command.MethodName),
                command.Kind,
                predicates,
                parameters,
                updates, createSource, resultTypeName, resultSource,
                command.SaveChanges ?? true, command.ReloadAfterSave, versions, command.WithOutcome, command.BypassedVisibility.ToArray(), contextKey, validations, insertColumns, conflictColumns,
                parameters.Select(static parameter => parameter.RuntimeType!).Concat(command.ResultProjection is null ? [] : new[] { command.ResultProjection.ReturnType }).Distinct().ToArray(),
                returning, returning ? entity.FindPrimaryKey()!.Properties.Select(static property => property.Name).ToArray() : null,
                ResolveCommandTimeout(settings, design.CommandTimeout, command.CommandTimeout, command.MethodName),
                collections.Length == 0 ? null : collections, WritePermissions(protectedWrites, allowed), batch,
                inMemoryCommand, savedResultKeys, createMany);
            if (inMemoryCommand?.Supported == true && translations?.Receipts.Count > 0)
            {
                var rendering = CreateInMemoryTranslatedCommand(design, command, configuredCommand, translations, modelDesign!);
                return configuredCommand with { InMemoryCommand = inMemoryCommand with { Translations = translations.Receipts, Rendering = rendering } };
            }
            return configuredCommand;
        }).ToArray();
    }

    private static LambdaExpression ReturningValue(IProperty property, LambdaExpression value)
    {
        var converter = property.GetRelationalTypeMapping().Converter;
        if (converter is null)
        {
            return value;
        }

        var nullable = Nullable.GetUnderlyingType(value.ReturnType);
        if (value.Body is ConstantExpression literal)
        {
            var type = (nullable is not null || !value.ReturnType.IsValueType) && converter.ProviderClrType.IsValueType && Nullable.GetUnderlyingType(converter.ProviderClrType) is null
                ? typeof(Nullable<>).MakeGenericType(converter.ProviderClrType)
                : converter.ProviderClrType;
            return Expression.Lambda(Expression.Constant(converter.ConvertToProvider(literal.Value), type), value.Parameters);
        }

        Expression input = nullable is null ? value.Body : Expression.Convert(value.Body, converter.ModelClrType);
        if (input.Type != converter.ModelClrType)
        {
            input = Expression.Convert(input, converter.ModelClrType);
        }

        var body = new ReturningValueSubstitution(converter.ConvertToProviderExpression.Parameters[0], input).Visit(converter.ConvertToProviderExpression.Body)!;
        if (nullable is not null || !value.ReturnType.IsValueType)
        {
            var providerType = !converter.ProviderClrType.IsValueType || Nullable.GetUnderlyingType(converter.ProviderClrType) is not null
                ? converter.ProviderClrType
                : typeof(Nullable<>).MakeGenericType(converter.ProviderClrType);
            body = Expression.Condition(Expression.Equal(value.Body, Expression.Constant(null, value.ReturnType)), Expression.Constant(null, providerType),
                body.Type == providerType ? body : Expression.Convert(body, providerType));
        }

        return Expression.Lambda(body, value.Parameters);
    }

    private static ConfiguredCommandTimeoutModel? ResolveCommandTimeout(RepositoryGenerationSettingsModel settings,
        RepositoryCommandTimeoutDesign? entityTimeout, RepositoryCommandTimeoutDesign? operationTimeout, string operation)
    {
        var configured = operationTimeout ?? entityTimeout;
        if (configured is null)
        {
            return null;
        }

        if (settings.ContractNamespace is null)
        {
            throw new InvalidOperationException($"RGD2302: Command timeout override on '{operation}' requires closed repositories so the operation owns the full query/stream lifetime and restores its selected context.");
        }

        return new(configured.Seconds, operationTimeout is null ? "entity" : "operation");
    }

    private sealed class ReturningValueSubstitution(ParameterExpression parameter, Expression value) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? value : base.VisitParameter(node);
    }

    private static void ValidateReturningMapping(IEntityType entity, LambdaExpression projection, string? providerName)
    {
        var table = entity.GetTableName();
        var schema = entity.GetSchema();
        if (table is null || entity.GetViewName() is not null || entity.GetSqlQuery() is not null || entity.FindPrimaryKey() is null || entity.BaseType is not null || entity.GetDerivedTypes().Any() ||
            entity.IsOwned() || entity.HasSharedClrType || entity.GetTableMappings().Count() != 1 || entity.GetDeclaredTriggers().Any() || entity.ClrType.IsAbstract ||
            entity.ClrType.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null) is null ||
            entity.Model.GetEntityTypes().Any(other => other != entity && other.GetTableName() == table && other.GetSchema() == schema))
        {
            throw new InvalidOperationException("RGD1306: Returning writes require one ordinary, unshared, non-inherited table with a key, a parameterless entity constructor, and no declared triggers.");
        }

        var store = StoreObjectIdentifier.Table(table, schema);
        var properties = entity.GetProperties().ToArray();
        if (properties.Any(property => property.PropertyInfo?.GetMethod is null || property.PropertyInfo.SetMethod is null || property.GetColumnName(store) is null) ||
            properties.Select(property => property.GetColumnName(store)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != properties.Length ||
            entity.FindAnnotation("SqlServer:MemoryOptimized")?.Value is true)
        {
            throw new InvalidOperationException("RGD1306: Returning writes require distinct mapped CLR scalar columns with getters/setters; shadow/backing-field-only and memory-optimized mappings are unsupported.");
        }

        if (providerName == "Microsoft.EntityFrameworkCore.Sqlite" && schema is not null)
        {
            throw new InvalidOperationException("RGD1306: SQLite returning writes do not support schemas.");
        }

        new ReturningProjectionValidator(entity, projection.Parameters[0]).Visit(projection.Body);
    }

    private sealed class ReturningProjectionValidator(IEntityType entity, ParameterExpression row) : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression == row)
            {
                if (entity.FindProperty(node.Member.Name) is null)
                {
                    throw new InvalidOperationException($"RGD1306: Returning projection '{node.Member.Name}' is not a mapped scalar. Native returned rows do not load navigations.");
                }

                return node;
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == row)
            {
                throw new InvalidOperationException("RGD1306: Returning projections must use mapped scalar values, not pass the entity to a mapping helper or return an entity graph.");
            }

            return base.VisitParameter(node);
        }
    }

    private static VisibilityPolicyModel[] CreateVisibilityPolicies(RepositoryEntityDesign? design)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return (design?.VisibilityPolicies ?? []).Select(policy =>
        {
            if (!names.Add(policy.Name))
            {
                throw new InvalidOperationException($"Duplicate visibility policy '{policy.Name}' on '{design!.EntityType.FullName}'.");
            }

            return new VisibilityPolicyModel(policy.Name, ProjectionExpressionRenderer.RenderBody(policy.Predicate, ["entity"]));
        }).ToArray();
    }

    private static ConfiguredInputValidationModel[] CreateInputValidations(IEnumerable<RepositoryInputValidationDesign> validations, List<FilterParameterModel> parameters)
        => validations.Select(validation => new ConfiguredInputValidationModel(RenderBoundExpression(validation.Predicate, parameters, skipRow: false),
            ProjectionExpressionRenderer.RenderBody(Expression.Lambda(Expression.Constant(validation.Message)), []))).ToArray();

    private static void ApplyInputNullability(string operation, IReadOnlyList<RepositoryInputNullabilityDesign> overrides, List<FilterParameterModel> parameters)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in overrides)
        {
            var name = ToParameterName(input.ParameterName);
            if (!names.Add(name))
            {
                throw new InvalidOperationException($"RGN0001: Operation '{operation}' configures duplicate nullable input '{name}'. Use one annotation tree per input.");
            }

            var index = parameters.FindIndex(parameter => parameter.Name == name);
            var clrTypeName = CSharpTypeName(input.InputType);
            if (index < 0 || parameters[index].TypeName != clrTypeName)
            {
                throw new InvalidOperationException($"RGN0002: Operation '{operation}' nullable input '{name}' must match a declared input of CLR type '{input.InputType.FullName}'.");
            }

            parameters[index] = parameters[index] with
            {
                TypeName = RepositoryTypeName.Annotated(input.Nullability),
                NullableReference = input.Nullability.NullableReference,
                ClrTypeName = clrTypeName
            };
        }
    }

    private static string ApplyResultNullability(string operation, RepositoryTypeNullabilityDesign? shape, Type type)
    {
        if (shape is null)
        {
            return CSharpTypeName(type);
        }

        if (shape.Type != type)
        {
            throw new InvalidOperationException($"RGN0004: Operation '{operation}' result nullability type '{shape.Type}' must match its final output value CLR type '{type}'.");
        }

        return RepositoryTypeName.Annotated(shape);
    }

    private static string AnnotateResultExpression(RepositoryTypeNullabilityDesign? shape, string typeName, string expression)
        => shape is null ? expression : "(" + typeName + ")(" + expression + ")";

    private static string? ApplyKeyNullability(RepositoryQueryDesign query)
    {
        var shape = query.DictionaryKeyNullability;
        if (shape is not null && (query.Dictionary is null || shape.Type != query.Dictionary.KeySelector.ReturnType || shape.NullableReference))
        {
            throw new InvalidOperationException($"RGN0006: Query '{query.MethodName}' key nullability must match a dictionary/lookup key CLR type with a non-null outer key. Only nested reference annotations may be nullable.");
        }

        return query.Dictionary is null ? null : shape is null ? CSharpTypeName(query.Dictionary.KeySelector.ReturnType) : RepositoryTypeName.Annotated(shape);
    }

    private static void ValidateVisibilityBypass(string operation, IReadOnlyList<string> bypasses, IEnumerable<string> available)
    {
        var known = available.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in bypasses)
        {
            if (!seen.Add(name))
            {
                throw new InvalidOperationException($"Operation '{operation}' bypasses visibility policy '{name}' more than once.");
            }

            if (!known.Contains(name))
            {
                throw new InvalidOperationException($"Operation '{operation}' cannot bypass unknown visibility policy '{name}' on its declared query roots.");
            }
        }
    }

    private static IProperty CommandProperty(IEntityType entity, LambdaExpression selector)
    {
        var body = selector.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
        {
            body = conversion.Operand;
        }

        if (body is not MemberExpression member || member.Expression != selector.Parameters[0] || entity.FindProperty(member.Member.Name) is not { } property)
        {
            throw new InvalidOperationException("Tracked mappings and version selectors require a direct mapped scalar property, not a navigation or computed selector.");
        }

        return property;
    }

    private static void ValidateCommandWrite(IProperty property, RepositoryCommandDesign command, HashSet<string> allowed,
        string[]? protectedWrites = null)
        => ValidateCommandWrite(property, command.Kind, command.MethodName, allowed, protectedWrites);

    private static void ValidateCommandWrite(IProperty property, RepositoryCommandKind kind, string methodName, HashSet<string> allowed,
        string[]? protectedWrites = null)
    {
        ValidateWriteProtection(property, methodName, allowed, protectedWrites);

        var create = kind is RepositoryCommandKind.Create or RepositoryCommandKind.InsertIfAbsent or RepositoryCommandKind.InsertOrUpdate;
        if (!create && property.IsKey())
        {
            throw new InvalidOperationException($"Patch cannot modify immutable key '{property.Name}'.");
        }

        if (property.ValueGenerated == ValueGenerated.OnAddOrUpdate || property.GetBeforeSaveBehavior() == PropertySaveBehavior.Ignore)
        {
            throw new InvalidOperationException($"'{property.Name}' is store-generated/read-only and cannot be assigned by this mapping.");
        }

        if ((property.IsKey() || property.IsForeignKey() || property.IsConcurrencyToken || property.ValueGenerated != ValueGenerated.Never) && !allowed.Contains(property.Name))
        {
            throw new InvalidOperationException($"Command '{methodName}' writes protected property '{property.Name}'. Declare AllowWrite() deliberately if the EF model permits it.");
        }

        if (!create && property.PropertyInfo?.SetMethod?.ReturnParameter.GetRequiredCustomModifiers().Any(static type => type.FullName == "System.Runtime.CompilerServices.IsExternalInit") == true)
        {
            throw new InvalidOperationException($"Patch cannot assign init-only property '{property.Name}'.");
        }
    }

    private sealed class CommandParameterUsage(ParameterExpression parameter) : ExpressionVisitor
    {
        public bool Used { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Used |= node == parameter;
            return node;
        }
    }

    private static ConfiguredPredicateModel[] CreateConfiguredPredicates(
        IEnumerable<LambdaExpression> predicates,
        out List<FilterParameterModel> parameters)
    {
        parameters = [];
        var result = new List<ConfiguredPredicateModel>();
        foreach (var predicate in predicates)
        {
            var parameterNames = predicate.Parameters
                .Skip(1)
                .Select((parameter, index) => ToParameterName(parameter.Name ?? "value" + (index + 1)))
                .ToArray();
            var expressionNames = new string[parameterNames.Length + 1];
            expressionNames[0] = "entity";
            parameterNames.CopyTo(expressionNames, 1);
            foreach (var parameter in predicate.Parameters.Skip(1).Select((value, index) => new
            {
                Name = parameterNames[index],
                Type = value.Type
            }))
            {
                AddParameter(parameters, parameter.Name, parameter.Type);
            }

            result.Add(new ConfiguredPredicateModel(
                ProjectionExpressionRenderer.RenderBody(predicate, expressionNames)));
        }

        return result.ToArray();
    }

    private static void AddParameter(
        List<FilterParameterModel> parameters,
        string name,
        Type type)
    {
        var typeName = CSharpTypeName(type);
        if (name.TrimStart('@').StartsWith("__repoGen", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Configured parameter '{name}' uses the reserved generated-local prefix '__repoGen'. Rename the lambda parameter.");
        }

        var existing = parameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, name, StringComparison.Ordinal));
        if (existing is null)
        {
            parameters.Add(new FilterParameterModel(name, typeName, RuntimeType: type));
            return;
        }

        if (!string.Equals(existing.TypeName, typeName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configured parameter '{name}' is used with both '{existing.TypeName}' and '{typeName}'.");
        }
    }

    private static ConfiguredDynamicSortModel? CreateConfiguredDynamicSort(
        IEntityType entity,
        RepositoryDynamicSortDesign? dynamicSort)
    {
        if (dynamicSort is null)
        {
            return null;
        }

        var fields = dynamicSort.PropertyNames.Select(propertyName =>
        {
            _ = entity.FindProperty(propertyName)
                ?? throw new InvalidOperationException(
                    $"Configured sortable field '{entity.ClrType.FullName}.{propertyName}' is not part of the EF model.");
            return new SortableFieldModel(propertyName);
        }).ToArray();
        return new ConfiguredDynamicSortModel(
            ToParameterName(dynamicSort.FieldNameParameter),
            ToParameterName(dynamicSort.DescendingParameter),
            fields);
    }

    private static IncludeModel[] CreateConfiguredQueryIncludes(
        IEntityType entity,
        RepositoryQueryDesign query,
        List<FilterParameterModel> parameters)
    {
        var includes = RepositoryIncludeSourceRenderer.Create(entity, query.Includes, parameters).ToList();
        if (query.Projection is not null)
        {
            includes.AddRange(CreateProjectionIncludes(entity, query.Projection));
        }

        return includes.ToArray();
    }

    private static string EnsureAsyncSuffix(string methodName) =>
        methodName.EndsWith("Async", StringComparison.Ordinal) ? methodName : methodName + "Async";

    private static string Pluralize(string value)
    {
        if (value.EndsWith("Dto", StringComparison.Ordinal))
        {
            return value + "s";
        }

        if (value.EndsWith('y') && value.Length > 1 &&
            "aeiou".IndexOf(char.ToLowerInvariant(value[value.Length - 2])) < 0)
        {
            return value[..^1] + "ies";
        }

        if (value.EndsWith('s') ||
            value.EndsWith('x') ||
            value.EndsWith('z') ||
            value.EndsWith("ch", StringComparison.Ordinal) ||
            value.EndsWith("sh", StringComparison.Ordinal))
        {
            return value + "es";
        }

        return value + "s";
    }

    internal static string ToParameterName(string propertyName)
    {
        if (propertyName.Length == 0)
        {
            return "value";
        }

        var result = char.ToLower(propertyName[0], CultureInfo.InvariantCulture) + propertyName[1..];
        return CSharpKeywords.Contains(result) ? "@" + result : result;
    }

    internal static string CSharpTypeName(Type type, bool nullable = false)
        => RepositoryTypeName.Clr(type) + (nullable && !type.IsValueType ? "?" : string.Empty);

    internal static string EscapeIdentifier(string value) =>
        CSharpKeywords.Contains(value) ? "@" + value : value;

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while"
    };
}
