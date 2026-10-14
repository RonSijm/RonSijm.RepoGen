using System.Text.Json;

namespace RonSijm.RepoGen.Tool;

internal static class RepositoryManifestRenderer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Render(RepositoryGenerationModel model)
    {
        var rendered = RenderInventory(model);
        if (model.Settings.ScriptedTestDoubleNamespace is not null || model.NavigationVisibility is not null || model.Settings.GeneratedNamespace is not null || model.Entities.Any(static entity => entity.CommandTimeout is not null || entity.ProtectedWriteProperties is not null ||
                                                                                                                                           entity.Commands.Any(static command => command.CommandTimeout is not null || command.Collections is not null || command.Batch is not null || command.CreateMany is not null) ||
                                                                                                                                           entity.Queries.Any(static query => query.CommandTimeout is not null || query.IgnoreAutoIncludes || query.Memberships is { Count: > 0 } || query.Target == Design.RepositoryQueryTarget.Write ||
                                                                                                                                                                              query.Dictionary is { DuplicateKeys: not Design.RepositoryDuplicateKeyBehavior.Throw } ||
                                                                                                                                                                              query.Cursor?.RequestParameterName is not null || query.CursorValidations is { Count: > 0 } ||
                                                                                                                                                                              query.Pipeline?.Stages.Any(static stage => stage.FieldSort is not null) == true)))
        {
            var inventory = System.Text.Json.Nodes.JsonNode.Parse(rendered)!;
            if (model.Settings.ScriptedTestDoubleNamespace is not null)
            {
                inventory["testDoubles"] = JsonSerializer.SerializeToNode(new
                {
                    mode = model.Settings.InMemoryTestCommands ? model.Settings.InMemoryTestQueries ? "PortableReadsAndWritesWithScriptedOverrides" : "PortableWritesWithScriptedOverrides" : model.Settings.InMemoryTestQueries ? "PortableReadsWithScriptedOverrides" : "Scripted",
                    @namespace = model.Settings.ScriptedTestDoubleNamespace,
                    executesDesignerPipeline = model.Settings.InMemoryTestQueries || model.Settings.InMemoryTestCommands,
                    repositories = ScriptedTestDoubleSourceRenderer.Entities(model).Select(entity => new
                    {
                        entity = entity.EntityTypeName,
                        fixture = model.Settings.ScriptedTestDoubleNamespace + "." + entity.EntityName + "RepositoryTestDouble",
                        operations = ScriptedTestDoubleSourceRenderer.Operations(model, entity).Select(operation => new
                        {
                            signature = operation.Method.Signature,
                            returns = operation.Method.ReturnType,
                            script = operation.ScriptName,
                            status = model.Settings.InMemoryTestQueries || model.Settings.InMemoryTestCommands ? InMemoryQuerySourceRenderer.Coverage(entity, operation).Status : "RequiresOverride",
                            reason = model.Settings.InMemoryTestQueries || model.Settings.InMemoryTestCommands ? InMemoryQuerySourceRenderer.Coverage(entity, operation).Reason : "Arrange a typed test handler/result before invocation; no query or persistence behavior is inferred.",
                            stream = operation.Stream
                        })
                    })
                });
                if (model.Settings.InMemoryTestStores)
                {
                    inventory["testDoubles"]!["stores"] = JsonSerializer.SerializeToNode(new
                    {
                        fixture = model.Settings.ScriptedTestDoubleNamespace + "." + InMemoryTestStoreSourceRenderer.FixtureName(model),
                        data = model.Settings.ScriptedTestDoubleNamespace + "." + InMemoryTestStoreSourceRenderer.DataName(model),
                        contextKey = model.Settings.ContextKeyTypeName,
                        lifetime = "ScopedUnlessExplicitlyRegistered",
                        copyPolicy = "ExplicitDetachedAdapter",
                        baselineScope = "AllStoresInOneContextPartition",
                        executesDesignerPipeline = model.Settings.InMemoryTestQueries || model.Settings.InMemoryTestCommands,
                        entities = ScriptedTestDoubleSourceRenderer.Entities(model).Where(static entity => entity.InMemoryStore is not null).Select(entity => new
                        {
                            entity = entity.EntityTypeName,
                            property = entity.EntityName + "Store",
                            row = entity.InMemoryStore!.RowTypeName,
                            shape = entity.InMemoryStore.RowTypeName == entity.EntityTypeName ? "EntityShape" : "ExplicitDataShape",
                            key = entity.InMemoryStore.KeyTypeName,
                            keySource = entity.InMemoryStore.KeySelectorSource,
                            copySource = entity.InMemoryStore.CopySource,
                            generatedValues = "ExplicitSeedKeys",
                            writes = model.Settings.InMemoryTestCommands ? "DeclaredTrackedCommandsOnly" : "FixtureArrangementOnly"
                        })
                    });
                    var storeReceipts = inventory["testDoubles"]!["stores"]!["entities"]!.AsArray();
                    var stores = ScriptedTestDoubleSourceRenderer.Entities(model).Where(static entity => entity.InMemoryStore is not null).ToArray();
                    for (var index = 0; index < stores.Length; index++)
                    {
                        if (stores[index].InMemoryStore!.KeyComparerSource is { } comparer)
                            storeReceipts[index]!["keyComparer"] = JsonSerializer.SerializeToNode(new
                            {
                                source = comparer,
                                scope = "FixtureKeyIdentityOnly",
                                evaluation = "OncePerStorePerContextPartition",
                                queryPredicatesChanged = false,
                                relationshipMatchingChanged = false,
                                contextKeyComparisonChanged = false,
                                relationalEquivalence = false
                            });
                        if (stores[index].InMemoryStore!.ValueEqualities is { Count: > 0 } equalities)
                            storeReceipts[index]!["valueEqualities"] = JsonSerializer.SerializeToNode(new
                            {
                                scope = "TrackedFixtureStateAndVersionsOnly", productionExpressions = "Unchanged",
                                keyIdentity = "Unchanged", queryPredicates = "Unchanged", relationshipMatching = "Unchanged",
                                copyAndRoundTrip = "OrdinaryStructuralEqualityStillRequired", providerEquivalence = false,
                                expressionExecutionDuringGeneration = "Never",
                                properties = equalities.Select(static equality => new { property = equality.PropertyName, source = equality.BodySource })
                            });
                        if (stores[index].InMemoryStore!.GeneratedValues is { Count: > 0 } values)
                        {
                            storeReceipts[index]!["generatedValues"] = values.Any(static value => value.ModelDefaultSource is not null) ? "ExplicitFactoriesAndOptedInModelConstants" : "ExplicitDesignerFactories";
                            storeReceipts[index]!["valueAdapters"] = JsonSerializer.SerializeToNode(new
                            {
                                properties = values.Select(value => new { property = value.PropertyName, onCreate = value.OnCreate, onUpdate = value.OnUpdate,
                                    insertCondition = value.CreateCondition, source = value.ValueSource }),
                                allocation = stores[index].Commands.Any(static command => command.IsAtomic && command.InMemoryCommand is { Supported: true, IsImmediate: true })
                                    ? "TrackedCreateSavedAndReservedImmediateInsertSavedOnly" : "DetachedSavedAndReservedRowsBeforeStage",
                                updates = stores[index].Commands.Any(static command => command.Kind is RonSijm.RepoGen.Design.RepositoryCommandKind.Update or RonSijm.RepoGen.Design.RepositoryCommandKind.InsertOrUpdate && command.InMemoryCommand is { Supported: true, IsImmediate: true })
                                    ? "AtomicSaveOrExplicitImmediatePreparation" : "AtomicSavePreparationOnly",
                                resets = "RowDerivedFactoriesNoExternalCounterReset",
                                relationalEquivalence = false
                            });
                            if (values.Any(static value => value.ModelDefaultSource is not null || value.CustomSentinelSource is not null))
                                storeReceipts[index]!["insertPolicies"] = JsonSerializer.SerializeToNode(new
                                {
                                    modelDefaults = values.Any(static value => value.ModelDefaultSource is not null) ? "OptInConstantInsertOnlyNotSqlIdentityComputedOrConstraints" : "NotSelected",
                                    ordering = "ExplicitFactoriesInDeclarationOrderThenModelConstantsByPropertyName",
                                    properties = values.Where(static value => value.ModelDefaultSource is not null || value.CustomSentinelSource is not null)
                                        .Select(static value => new { property = value.PropertyName, modelDefault = value.ModelDefaultSource, sentinel = value.CustomSentinelSource }),
                                    suppliedValues = "PreserveUnlessEqualToModelSentinel", saving = "NoDefaultOnUpdateOrSeed", relationalEquivalence = false
                                });
                        }
                        if (stores[index].InMemoryStore!.WriteMapping is { } writeMapping)
                        {
                            storeReceipts[index]!["writeMapping"] = JsonSerializer.SerializeToNode(new
                            {
                                kind = "ExplicitScalarWriteAdapter",
                                source = writeMapping.WriteSource,
                                savedData = "CurrentDetachedRowOnUpdateNullOnCreate",
                                preparation = "BeforeAnySharedStoreCommit",
                                roundTrip = "FullScalarAndPrimaryKeyValidation",
                                constructsPersistenceEntity = false,
                                reverseProjectionInferred = false,
                                navigationWrites = false,
                                relationalEquivalence = false
                            });
                        }
                        if (stores[index].InMemoryStore!.EntityMapping is { } mapping)
                        {
                            storeReceipts[index]!["readMapping"] = JsonSerializer.SerializeToNode(new
                            {
                                kind = "ExplicitScalarAssignments",
                                readRow = mapping.ReadTypeName,
                                members = mapping.Members.Select(static member => member.Name),
                                constructsPersistenceEntity = false,
                                writes = "NotAdapted"
                            });
                            if (mapping.Navigations is { Count: > 0 } navigationMembers)
                                storeReceipts[index]!["readMapping"]!["navigationSlots"] = JsonSerializer.SerializeToNode(navigationMembers.Select(static member => new
                                { name = member.Name, targetReadRow = member.TargetTypeName, collection = member.IsCollection, required = member.Required }));
                        }
                    }
                }
                if (model.Settings.InMemoryTestQueries || model.Settings.InMemoryTestCommands)
                {
                    inventory["testDoubles"]!["execution"] = JsonSerializer.SerializeToNode(new
                    {
                        semantics = "LINQ-to-Objects",
                        reads = model.Entities.SelectMany(static entity => entity.Queries).Any(static query => query.InMemoryQuery is { Supported: true } coverage && (coverage.Source is not null || coverage.Navigation is not null))
                            ? "OneDetachedParticipatingStoreSnapshotPerInvocation"
                            : "OneDetachedStoreSnapshotPerInvocation",
                        partitions = "DesignerContextKey",
                        overrides = "MatchingRulesThenExplicitHandlerThenGeneratedBackend",
                        reset = "ClearsScriptsAndCallsRetainsBackendAndFixtureData",
                        caching = "NotSimulated",
                        tracking = "DetachedCopiesOnly",
                        relationalGuarantees = "NotSimulated",
                        strictQueries = model.Settings.RequirePortableTestQueries
                    });
                    var repositories = inventory["testDoubles"]!["repositories"]!.AsArray();
                    var selectedEntities = ScriptedTestDoubleSourceRenderer.Entities(model).ToArray();
                    for (var entityIndex = 0; entityIndex < selectedEntities.Length; entityIndex++)
                    {
                        var operations = ScriptedTestDoubleSourceRenderer.Operations(model, selectedEntities[entityIndex]);
                        for (var operationIndex = 0; operationIndex < operations.Count; operationIndex++)
                        {
                            var query = InMemoryQuerySourceRenderer.Query(selectedEntities[entityIndex], operations[operationIndex]);
                            var write = InMemoryCommandSourceRenderer.Command(selectedEntities[entityIndex], operations[operationIndex]);
                            repositories[entityIndex]!["operations"]![operationIndex]!["reasons"] = JsonSerializer.SerializeToNode(
                                query?.InMemoryQuery?.Reasons ?? InMemoryCommandSourceRenderer.Command(selectedEntities[entityIndex], operations[operationIndex])?.InMemoryCommand?.Reasons ??
                                [InMemoryQuerySourceRenderer.Coverage(selectedEntities[entityIndex], operations[operationIndex]).Reason]);
                            if (query?.InMemoryQuery is { Supported: true, ModelQueryFilters: { Count: > 0 } modelFilters })
                                repositories[entityIndex]!["operations"]![operationIndex]!["modelQueryFilters"] = JsonSerializer.SerializeToNode(new
                                {
                                    optIn = "UseInMemoryModelQueryFilter",
                                    roots = modelFilters,
                                    application = query.InMemoryQuery.Navigation?.Roots.Any(static root => root.ModelQueryFilter is not null) == true
                                        ? "BeforeDeclaredNavigationLoadingThenDesignerVisibilityAndQueryPipeline" : "BeforeDesignerVisibilityAndQueryPipeline",
                                    bypass = "IgnoreQueryFiltersOnlyNotBypassVisibility",
                                    scope = query.InMemoryQuery.Navigation?.Roots.Any(static root => root.ModelQueryFilter is not null) == true
                                        ? "OptedInOrdinaryCollectionsAndOptionalReferencesNoRequiredFilteredReferencesManyToManyOrWrites" : "RowOnlyReadRootsNoFilteredNavigationGraphsOrWrites",
                                    capturedContext = "NotEvaluated",
                                    providerGuarantees = "NotSimulated"
                                });
                            if (query?.InMemoryQuery is { Supported: true, Navigation: { } filteredNavigation } &&
                                filteredNavigation.Roots.Any(static root => root.ModelQueryFilter is not null))
                                repositories[entityIndex]!["operations"]![operationIndex]!["modelQueryFilters"]!["navigationOptIn"] =
                                    "UseInMemoryModelQueryFilter(includeNavigations: true)OnEveryFilteredParticipant";
                            if (write?.InMemoryCommand is { Supported: true, ModelQueryFilter: { } commandFilter })
                                repositories[entityIndex]!["operations"]![operationIndex]!["modelQueryFilter"] = JsonSerializer.SerializeToNode(new
                                {
                                    optIn = "PerCommandUseInMemoryModelQueryFilter",
                                    root = commandFilter.Root,
                                    selection = write.Kind == RonSijm.RepoGen.Design.RepositoryCommandKind.Create ? "NoInsertFilter" : "SavedRowsBeforeDesignerVisibilityAndPredicates",
                                    savedResults = write.InMemoryCommand.SavedResult is null ? "NoFreshQuery" : "ReappliedToFreshSavedRows",
                                    bypass = "DesignerVisibilityBypassDoesNotBypassModelFilter",
                                    saving = "SharedSaveDoesNotRecheckQueryFilter",
                                    scope = write.InMemoryCommand.SavedResult?.ModelQueryFilters is { Count: > 0 }
                                        ? "ScalarWritesAndFilteredSavedReadsNoGraphMutationsNativeReturningOrUpsert"
                                        : write.InMemoryCommand.IsImmediate && write.IsReturning
                                        ? "ScalarNativeReturningSavedSelectionNoFreshResultFilterGraphOrUpsert" : "ScalarWritesNoFilteredGraphsNativeReturningOrUpsert",
                                    capturedContext = "NotEvaluated",
                                    providerGuarantees = "NotSimulated"
                                });
                            if (write?.InMemoryCommand is { Supported: true, SavedResult.ModelQueryFilters: { Count: > 0 } savedFilters })
                                repositories[entityIndex]!["operations"]![operationIndex]!["savedModelQueryFilters"] = JsonSerializer.SerializeToNode(new
                                {
                                    optIn = "PerCommandUseInMemoryModelQueryFilter(includeNavigations: true)",
                                    roots = savedFilters,
                                    relatedOptIn = "PerEntityUseInMemoryModelQueryFilter(includeNavigations: true)",
                                    application = "AfterSharedSaveBeforeNavigationLoadingAndResultVisibility",
                                    rootReadPermission = "CommandOwnedNotInheritedFromEntityReads",
                                    mutations = "ScalarOnlyNoFilteredReconciliationOrGraphPersistence",
                                    missingResults = "PostSaveFailureNeverSilentlyShortenBatches",
                                    providerGuarantees = "NotSimulated"
                                });
                            if (write?.InMemoryCommand is { Supported: true, IsImmediate: true } && !write.IsAtomic)
                                repositories[entityIndex]!["operations"]![operationIndex]!["immediateSimulation"] = JsonSerializer.SerializeToNode(new
                                {
                                    optIn = "SimulateImmediateWrites",
                                    targets = "SavedVisibleMatchingRowsOnly",
                                    assignments = "AllSettersReadOriginalRow",
                                    returning = write.IsReturning ? write.Kind == RonSijm.RepoGen.Design.RepositoryCommandKind.Delete ? "DeletedScalarRows" : "UpdatedScalarRows" : "AffectedCount",
                                    preparation = "ValuesFactoriesMappingsAndResultsBeforeCommit",
                                    pendingWrites = "NeitherFlushedNorRefreshed",
                                    transactions = "FixtureCheckpointRollback",
                                    order = "Unspecified",
                                    providerGuarantees = "NotSimulated",
                                    relationalEquivalence = false
                                });
                            if (write?.InMemoryCommand is { Supported: true, IsImmediate: true, ModelQueryFilter: not null } && write.IsReturning && !write.IsAtomic)
                                repositories[entityIndex]!["operations"]![operationIndex]!["modelQueryFilter"]!["returning"] =
                                    write.Kind == RonSijm.RepoGen.Design.RepositoryCommandKind.Delete
                                        ? "MatchedPreDeleteValuesNoPostMutationFilter" : "MatchedPostUpdateValuesNoPostMutationFilter";
                            if (write?.InMemoryCommand is { Supported: true, IsImmediate: true } && write.IsAtomic)
                                repositories[entityIndex]!["operations"]![operationIndex]!["atomicSimulation"] = JsonSerializer.SerializeToNode(new
                                {
                                    optIn = "SimulateImmediateWrites",
                                    conflicts = "DeclaredTargetOverAllSavedRowsOrdinaryStructuralEquality",
                                    visibility = "UpdateBranchOnlyNeverInsertionOrConflictProbe",
                                    assignments = "AllSettersReadOriginalRow",
                                    outcomes = "InsertedUpdatedAlreadyExistsNotMatched",
                                    generatedValues = "ExplicitFactoriesSavedRowsOnlyNoPendingReservations",
                                    preparation = "ValuesFactoriesMappingsAndResultsBeforeCommit",
                                    pendingWrites = "NeitherFlushedNorRefreshed",
                                    transactions = "FixtureCheckpointRollback",
                                    providerGuarantees = "NotSimulated",
                                    relationalEquivalence = false
                                });
                            if (write?.InMemoryCommand is { Supported: true, Collections: { Count: > 0 } } collectionCommand)
                                repositories[entityIndex]!["operations"]![operationIndex]!["collectionSimulation"] = JsonSerializer.SerializeToNode(new
                                {
                                    optIn = "SimulateCollectionWrites",
                                    targets = "OwnerScopedVisibleSavedChildren",
                                    ownership = "CompleteMappedPrincipalToForeignKeysNeverInput",
                                    matching = "DefaultClrEquality",
                                    collections = write.Collections!.Select((collection, index) => new
                                    {
                                        navigation = collection.NavigationName, childStore = collectionCommand.Collections[index].StoreName,
                                        maximumItems = collection.MaximumItems, missingItems = collection.MissingItems.ToString(),
                                        additions = collection.CreateSource is not null, updates = collection.Updates.Count != 0,
                                        conditional = collection.ConditionSource is not null
                                    }),
                                    pendingTargets = "RejectOwnerScopedModifiedAddedDeletedChildrenIncludingHidden",
                                    saving = "SharedUnitOfWorkIncludesUnrelatedPendingWrites",
                                    rollback = "RestoreCommandOnlyBeforeSaveCheckpointForSavedRows",
                                    results = write.SavedResultKeys is null ? "SavedScalarParentProjectionOnly" : "ExplicitReturnsSavedQuery",
                                    entityGraphs = "NotPersistedOrInferred",
                                    providerGuarantees = "NotSimulated",
                                    relationalEquivalence = false
                                });
                            if (write?.InMemoryCommand is { Supported: true, SavedResult: { } savedResult })
                                repositories[entityIndex]!["operations"]![operationIndex]!["savedResultQuery"] = JsonSerializer.SerializeToNode(new
                                {
                                    optIn = "ReturnsSaved",
                                    key = "CompleteSavedPrimaryKeyNotMutationPredicate",
                                    state = "SavedRowsAfterSharedUnitOfWorkSave",
                                    capture = "DetachedParticipatingTablesInOneFixturePartition",
                                    rootVisibility = "DeclaredPoliciesUnlessExplicitlyBypassed",
                                    relatedVisibility = "ExplicitInProjectionNoImplicitPolicyInjection",
                                    relationships = savedResult.Navigation?.Relationships.Select(static edge => edge.Name) ?? [],
                                    failure = "RepositorySavedResultExceptionWriteAlreadySaved",
                                    atomicReturning = false,
                                    providerGuarantees = "NotSimulated"
                                });
                            if ((write?.Batch is not null || write?.CreateMany is not null) && write!.InMemoryCommand is { Supported: true, SavedResult: not null })
                            {
                                var receipt = repositories[entityIndex]!["operations"]![operationIndex]!["savedResultQuery"]!;
                                receipt["batchOrder"] = "OneResultPerInputInInputOrder";
                                receipt["batchCapture"] = "OneCoordinatedSnapshotForAllSavedInputKeys";
                            }
                            if (write?.InMemoryCommand is { Supported: true, SavedResult.Navigation: { } savedNavigation } && savedNavigation.Relationships.Any(static edge => edge.Join is not null))
                                repositories[entityIndex]!["operations"]![operationIndex]!["savedResultQuery"]!["joinRelationships"] =
                                    JsonSerializer.SerializeToNode(savedNavigation.Relationships.Where(static edge => edge.Join is not null).Select(InMemoryNavigationManifestRenderer.Relationship));
                            if ((query?.InMemoryQuery?.Translations ?? write?.InMemoryCommand?.Translations) is { Count: > 0 } translations)
                                repositories[entityIndex]!["operations"]![operationIndex]!["expressionTranslations"] = JsonSerializer.SerializeToNode(new
                                {
                                    matching = "ExactClosedMethodOverloadAndFixedCallShape",
                                    semantics = "DesignerDeclaredFixtureOnlyNotProviderEquivalence",
                                    productionExpressions = "Unchanged",
                                    expressionExecutionDuringGeneration = "Never",
                                    translations
                                });
                            if (query?.InMemoryQuery?.Source is { } source)
                                repositories[entityIndex]!["operations"]![operationIndex]!["sourceSnapshot"] = JsonSerializer.SerializeToNode(new
                                {
                                    scope = "ParticipatingStoresInOneContextPartition",
                                    capture = "DetachedCopiesUnderOneContextLock",
                                    roots = source.Roots.Select(static root => root.EntityTypeName),
                                    operators = source.Operators,
                                    visibility = "EachRootBeforeComposition"
                                });
                            if (query?.InMemoryQuery?.Navigation is { } navigation)
                                repositories[entityIndex]!["operations"]![operationIndex]!["navigationSnapshot"] = JsonSerializer.SerializeToNode(new
                                {
                                    scope = "ParticipatingStoresInOneContextPartition",
                                    capture = "DetachedCopiesUnderOneContextLock",
                                    loading = "ExplicitMappedKeyRelationshipsOnly",
                                    roots = navigation.Roots.Select(static root => root.EntityTypeName),
                                    relationships = navigation.Relationships.Select(InMemoryNavigationManifestRenderer.Relationship),
                                    relatedVisibility = "NoImplicitRepoGenPolicyInjection",
                                    writes = "NotAdapted"
                                });
                        }
                    }
                }
                if (model.Settings.InMemoryTestCommands)
                {
                    inventory["testDoubles"]!["writeSimulation"] = JsonSerializer.SerializeToNode(new
                    {
                        operations = "ExplicitTrackedScalarCreatePatchDelete",
                        batches = "ExplicitBoundedPatchManyLeafDeleteMany",
                        batchMatching = "DeclaredUniqueScalarInputOrder",
                        batchTargets = "RejectPendingModificationsAllTargetsRequired",
                        reads = "SavedRowsOnly",
                        stage = "PendingAssignmentsSeparateFromStoreRows",
                        save = "AtomicPreparationThenSharedStoreCommit",
                        unitOfWork = "ExplicitConsumerRegistrationForOneFixturePartition",
                        transactions = "SharedFixtureCheckpointRollbackNoParallelIsolation",
                        generatedValues = "NotInferred",
                        versions = "ExplicitOrdinaryScalarBinaryTokens",
                        caches = "NotSimulated",
                        strictCommands = model.Settings.RequirePortableTestCommands
                    });
                    if (model.Entities.SelectMany(static entity => entity.Commands).Any(static command => !command.IsAtomic && command.InMemoryCommand is { Supported: true, IsImmediate: true }))
                        inventory["testDoubles"]!["writeSimulation"]!["immediate"] = "PerCommandExplicitSavedScalarUpdateDeleteReturning";
                    if (model.Entities.SelectMany(static entity => entity.Commands).Any(static command => command.IsAtomic && command.InMemoryCommand is { Supported: true, IsImmediate: true }))
                        inventory["testDoubles"]!["writeSimulation"]!["atomic"] = "PerCommandExplicitSavedScalarInsertIfAbsentInsertOrUpdate";
                    if (model.Entities.SelectMany(static entity => entity.Commands).Any(static command => command.InMemoryCommand is { Supported: true, Collections: { Count: > 0 } }))
                        inventory["testDoubles"]!["writeSimulation"]!["collections"] = "PerCommandExplicitBoundedOwnerScopedLeafChildStores";
                }
            }
            if (model.Settings.GeneratedNamespace is not null)
            {
                inventory["generatedNamespace"] = model.Settings.GeneratedNamespace;
            }

            if (model.NavigationVisibility is not null)
            {
                inventory["navigationVisibility"] = JsonSerializer.SerializeToNode(new
                {
                    requiredByDefault = model.Settings.RequireNavigationVisibilityReview,
                    operations = model.NavigationVisibility.Select(static operation => new
                    {
                        entity = operation.Entity,
                        operation = operation.Operation,
                        kind = operation.Kind,
                        required = operation.Required,
                        accesses = operation.Accesses.Select(static access => new
                        {
                            declaringEntity = access.DeclaringEntity,
                            navigation = access.Navigation,
                            targetEntity = access.TargetEntity,
                            policy = access.Policy,
                            reviewReason = access.ReviewReason,
                            reviewScope = access.ReviewScope
                        })
                    })
                });
            }

            var entities = inventory["entities"]!.AsArray();
            for (var index = 0; index < model.Entities.Count; index++)
            {
                var entity = model.Entities[index];
                if (entity.ProtectedWriteProperties is { } protectedWrites)
                {
                    entities[index]!["protectedWriteProperties"] = JsonSerializer.SerializeToNode(protectedWrites);
                }

                if (entity.CommandTimeout is not null)
                {
                    entities[index]!["commandTimeout"] = TimeoutInventory(entity.CommandTimeout);
                }

                var commands = entities[index]!["commands"]!.AsArray();
                var configuredCommands = entity.Commands.OrderBy(static command => command.MethodName, StringComparer.Ordinal).ToArray();
                for (var commandIndex = 0; commandIndex < configuredCommands.Length; commandIndex++)
                {
                    if (configuredCommands[commandIndex].CreateMany is { } creation)
                    {
                        commands[commandIndex]!["createMany"] = JsonSerializer.SerializeToNode(new
                        {
                            item = creation.ItemTypeName,
                            maximumItems = creation.MaximumItems,
                            preparation = "AllMappedRowsBeforeAnyStaging",
                            emptyInput = "NoChangesBeforeContextAcquisition",
                            saveScope = "sharedContext",
                            resultOrder = "input",
                            matchingKeyRequired = false,
                            nativeBulkInsert = false,
                            graphInsertion = false
                        });
                    }

                    if (configuredCommands[commandIndex].Batch is { } batch)
                    {
                        commands[commandIndex]!["batch"] = JsonSerializer.SerializeToNode(new
                        {
                            item = batch.ItemTypeName,
                            key = batch.KeyPropertyName,
                            maximumItems = batch.MaximumItems,
                            updates = batch.Updates.Select(static update => update.PropertyName),
                            expectedVersions = batch.ExpectedVersions.Select(static version => version.PropertyName),
                            missingItems = "NotFound",
                            saveScope = "sharedContext",
                            resultOrder = "input"
                        });
                    }

                    if (configuredCommands[commandIndex].AllowedProtectedWrites is { } permissions)
                    {
                        commands[commandIndex]!["allowedProtectedWrites"] = JsonSerializer.SerializeToNode(permissions);
                    }

                    if (configuredCommands[commandIndex].CommandTimeout is { } commandTimeout)
                    {
                        commands[commandIndex]!["commandTimeout"] = TimeoutInventory(commandTimeout);
                    }

                    if (configuredCommands[commandIndex].Collections is { } collections)
                    {
                        var collectionInventory = JsonSerializer.SerializeToNode(collections.Select(static collection => new
                        {
                            navigation = collection.NavigationName,
                            child = collection.ChildTypeName,
                            key = collection.ChildKeySource,
                            maximumItems = collection.MaximumItems,
                            missingItems = collection.MissingItems.ToString(),
                            add = collection.CreateSource is not null,
                            updates = collection.Updates.Select(static update => update.PropertyName),
                            expectedVersions = collection.ExpectedVersions.Select(static version => version.PropertyName),
                            conditional = collection.ConditionSource is not null,
                            visibilityPolicies = collection.VisibilityPredicates.Count,
                            cacheTag = collection.ChildCacheTag
                        }), Options)!.AsArray();
                        for (var collectionIndex = 0; collectionIndex < collections.Count; collectionIndex++)
                        {
                            if (collections[collectionIndex].Link is { } link)
                            {
                                collectionInventory[collectionIndex]!["linkReconciliation"] = JsonSerializer.SerializeToNode(new
                                {
                                    target = link.TargetTypeName, ownerForeignKeys = link.OwnerForeignKeys,
                                    targetForeignKeys = link.TargetForeignKeys, targetPrincipalKeys = link.TargetPrincipalKeys,
                                    targets = "ExistingReferencesOnlyNeverCreatedUpdatedOrDeleted",
                                    targetPolicies = "NotImplicitlyInjected", foreignKeys = "DatabaseEnforcedNotFixtureSimulated",
                                    mutation = "TrackedExplicitJoinRowsNotSkipNavigationGraph", nullTargets = "Unsupported"
                                });
                                if (link.SharedReferenceColumns is { Count: > 0 } shared)
                                    collectionInventory[collectionIndex]!["linkReconciliation"]!["sharedReferenceColumns"] = JsonSerializer.SerializeToNode(new
                                    {
                                        columns = shared.Select(static column => new { property = column.PropertyName, ownerProperty = column.OwnerPropertyName, referenceKeyIndex = column.ReferenceKeyIndex }),
                                        matching = "CompleteTargetKeyMustAgreeWithSelectedOwner",
                                        comparison = "DefaultClrMatchingNotProviderOrFixtureKeyComparison",
                                        validation = "AllSuppliedKeysBeforeCollectionLoadingOrMutation",
                                        ownership = "BoundFromSelectedOwnerNeverTransferredOrNormalized"
                                    });
                                if (link.TargetNavigationName is { } targetNavigation)
                                    collectionInventory[collectionIndex]!["linkReconciliation"]!["targetNavigation"] = targetNavigation;
                                if (link.AdditionalReferences is { Count: > 0 } additionalReferences)
                                {
                                    collectionInventory[collectionIndex]!["linkReconciliation"]!["additionalReferences"] = JsonSerializer.SerializeToNode(new
                                    {
                                        references = additionalReferences.Select(static reference => new
                                        {
                                            navigation = reference.NavigationName, target = reference.TypeName,
                                            foreignKeys = reference.ForeignKeys, principalKeys = reference.PrincipalKeys
                                        }),
                                        initialization = "EveryScalarComponentExplicitOnNewLinksOnly",
                                        mutation = "ImmutableOnMatchedLinksNeverWriteReferencedEntities",
                                        existence = "DatabaseEnforcedNotFixtureSimulated", authorization = "NotImplicitlyInjected"
                                    });
                                    var referenceInventory = collectionInventory[collectionIndex]!["linkReconciliation"]!["additionalReferences"]!["references"]!.AsArray();
                                    for (var referenceIndex = 0; referenceIndex < additionalReferences.Count; referenceIndex++)
                                        if (additionalReferences[referenceIndex].Optional == true)
                                        {
                                            referenceInventory[referenceIndex]!["optional"] = true;
                                            referenceInventory[referenceIndex]!["nullSemantics"] = "AllNullAbsentAllPresentReferencePartialNullRejectedOnNewLinks";
                                            referenceInventory[referenceIndex]!["validation"] = "ApprovedInitializerOnceOnNewLinkBeforeThatRowIsStaged";
                                        }
                                }
                            }
                            if (collections[collectionIndex].MatchingKeyProperties is { } matchingKey)
                                collectionInventory[collectionIndex]!["compositeMatchingKey"] = JsonSerializer.SerializeToNode(new
                                {
                                    properties = matchingKey, equality = "DefaultClrValueTuple", nullComponents = "RejectBeforeMutation",
                                    scope = "SelectedOwnerVisibleChildren", mutable = false
                                });
                            if (collections[collectionIndex].ProtectedWriteProperties is { } childProtection)
                            {
                                collectionInventory[collectionIndex]!["protectedWriteProperties"] = JsonSerializer.SerializeToNode(childProtection);
                                collectionInventory[collectionIndex]!["allowedProtectedWrites"] = JsonSerializer.SerializeToNode(collections[collectionIndex].AllowedProtectedWrites);
                            }
                        }

                        commands[commandIndex]!["collections"] = collectionInventory;
                    }
                }

                var queries = entities[index]!["queries"]!.AsArray();
                var configuredQueries = model.Entities[index].Queries.OrderBy(static query => query.MethodName, StringComparer.Ordinal).ToArray();
                for (var queryIndex = 0; queryIndex < configuredQueries.Length; queryIndex++)
                {
                    var query = configuredQueries[queryIndex];
                    if (query.IgnoreAutoIncludes)
                    {
                        queries[queryIndex]!["ignoreAutoIncludes"] = true;
                    }

                    if (query.CommandTimeout is not null)
                    {
                        queries[queryIndex]!["commandTimeout"] = TimeoutInventory(query.CommandTimeout);
                    }

                    if (query.Target == Design.RepositoryQueryTarget.Write)
                    {
                        queries[queryIndex]!["repositoryTarget"] = "write";
                    }

                    if (query.Cursor?.RequestParameterName is not null)
                    {
                        queries[queryIndex]!["cursorRequest"] = JsonSerializer.SerializeToNode(new
                        {
                            parameter = query.Cursor.RequestParameterName,
                            seek = query.Cursor.SeekPredicateBodySource,
                            mapping = query.Cursor.CursorSelectorExpressionSource
                        });
                    }

                    if (query.CursorValidations is { Count: > 0 })
                    {
                        queries[queryIndex]!["cursorValidations"] = JsonSerializer.SerializeToNode(query.CursorValidations
                            .Select(static validation => new { predicate = validation.PredicateSource, message = validation.MessageSource }));
                    }

                    if (query.Dictionary is { DuplicateKeys: not Design.RepositoryDuplicateKeyBehavior.Throw } dictionary)
                    {
                        queries[queryIndex]!["dictionaryDuplicateKeys"] = dictionary.DuplicateKeys.ToString();
                    }

                    if (query.Pipeline?.Stages.Any(static stage => stage.FieldSort is not null) == true)
                    {
                        queries[queryIndex]!["resultFieldSorting"] = JsonSerializer.SerializeToNode(query.Pipeline.Stages
                            .Select((stage, stageIndex) => new { stage, stageIndex }).Where(static item => item.stage.FieldSort is not null)
                            .Select(static item => new
                            {
                                stage = item.stageIndex,
                                input = item.stage.InputTypeName,
                                field = item.stage.FieldSort!.FieldNameSource,
                                descending = item.stage.FieldSort.DescendingSource,
                                fields = item.stage.FieldSort.Fields.Select(static field => new { alias = field.Name, expression = field.KeySelectorExpressionSource }),
                                unspecified = item.stage.FieldSort.DefaultField is not null ? "defaultField" : item.stage.FieldSort.PreserveOrderWhenUnspecified ? "preserveOrder" : "reject",
                                defaultField = item.stage.FieldSort.DefaultField,
                                defaultDescending = item.stage.FieldSort.DefaultDescending
                            }));
                    }

                    if (query.Memberships is not { Count: > 0 })
                    {
                        continue;
                    }

                    // Method names can be overloaded; the stable inventory order identifies the exact operation.
                    var target = queries[queryIndex]!;
                    target["membershipInputs"] = JsonSerializer.SerializeToNode(query.Memberships!.Select(static membership => new
                    {
                        element = membership.ElementTypeName,
                        values = membership.ValuesSource,
                        negated = membership.Negated,
                        nullInput = membership.Options.NullInput.ToString(),
                        emptyInput = membership.Options.EmptyInput.ToString(),
                        nullValues = membership.Options.NullValues.ToString(),
                        distinct = membership.Options.Distinct,
                        maximumRawCount = membership.Options.MaximumCount
                    }));
                }
            }

            rendered = inventory.ToJsonString(Options);
        }

        return rendered + Environment.NewLine;
    }

    private static System.Text.Json.Nodes.JsonNode? TimeoutInventory(ConfiguredCommandTimeoutModel timeout) =>
        JsonSerializer.SerializeToNode(new { seconds = timeout.Seconds, source = timeout.Source });

    private static string RenderInventory(RepositoryGenerationModel model) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        generatedBy = "RonSijm.RepoGen",
        context = model.ContextTypeName,
        surface = model.Settings.ContractNamespace is not null ? "closedOperations" : model.Settings.CqrsSelectors ? "cqrsSelectors" : "selectors",
        contractNamespace = model.Settings.ContractNamespace,
        contextLifetime = model.Settings.UseAsyncContextLeases ? "asyncLease" : model.Settings.UseDbContextResolver ? "resolver" : "borrowed",
        contextKeyType = model.Settings.ContextKeyTypeName,
        commandTimeoutSeconds = model.Settings.CommandTimeoutSeconds,
        features = new
        {
            cacheDecorator = model.Settings.GenerateCacheDecorator,
            closedCaching = model.Settings.ClosedCaching,
            atomicWriter = model.Settings.AtomicWriterTypeName,
            batchOperations = model.Settings.GenerateBatchOperations,
            setBasedSelectorOperations = model.Settings.GenerateSetBasedOperations,
            concurrencyRetry = model.Settings.GenerateConcurrencyRetry,
            transactionHelpers = model.Settings.GenerateTransactions,
            synchronousMethods = model.Settings.GenerateSynchronousMethods,
            selectorTrackingMethods = model.Settings.GenerateTrackingMethods
        },
        tracking = new
        {
            defaultSelector = model.Settings.DefaultTrackingBehavior.ToString(),
            defaultReadOnly = model.Settings.ReadOnlyTrackingBehavior.ToString(),
            noTrackingMethod = model.Settings.GenerateTrackingMethods ? model.Settings.NoTrackingMethodName : null,
            trackingMethod = model.Settings.GenerateTrackingMethods ? model.Settings.TrackingMethodName : null,
            noTrackingWithIdentityResolutionMethod = model.Settings.GenerateTrackingMethods ? model.Settings.NoTrackingWithIdentityResolutionMethodName : null
        },
        entities = model.Entities.Select(entity => new
        {
            entity = entity.EntityTypeName,
            readEnabled = entity.ReadEnabled,
            writeEnabled = entity.WriteEnabled,
            readPorts = (entity.ReadPorts ?? []).Select(static port => port.TypeName),
            writePorts = (entity.WritePorts ?? []).Select(static port => port.TypeName),
            visibilityPolicies = (entity.VisibilityPolicies ?? []).Select(static policy => policy.Name),
            loadProfiles = (entity.LoadProfiles ?? []).Select(static profile => new
            {
                name = profile.Name,
                paths = profile.Includes.SelectMany(static include => include.Paths ?? []).Select(static path => path.Path).Distinct(StringComparer.Ordinal)
            }),
            defaultLoading = entity.Includes.SelectMany(static include => include.Paths ?? [])
                .GroupBy(static path => path.Path, StringComparer.Ordinal)
                .Select(static group => new { path = group.Key, collection = group.Any(static path => path.IsCollection), filtered = group.Any(static path => path.FilterSource is not null) }),
            queries = entity.Queries.OrderBy(static query => query.MethodName, StringComparer.Ordinal).Select(query => new
            {
                name = query.MethodName,
                result = PublicType(ProjectionSourceRenderer.RenderQueryReturnType(query), model.Settings.ContractNamespace is not null, query.InfrastructurePrefix),
                parameters = ProjectionSourceRenderer.CreateQueryParameters(query).Select(parameter => new { name = parameter.Name, type = PublicType(parameter.Type, model.Settings.ContractNamespace is not null, query.InfrastructurePrefix) }),
                tracking = query.TrackingBehavior.ToString(),
                useDefaultSort = query.UseDefaultSort,
                loadProfiles = query.LoadProfileNames ?? [],
                loading = (query.Source is null ? entity.Includes.Concat(query.Includes) : query.Includes).SelectMany(static include => include.Paths ?? [])
                    .GroupBy(static path => path.Path, StringComparer.Ordinal)
                    .Select(static group => new { path = group.Key, collection = group.Any(static path => path.IsCollection), filtered = group.Any(static path => path.FilterSource is not null) }),
                loadingStrategy = query.SplittingBehavior.ToString(),
                sources = query.Source?.EntityTypeNames ?? [entity.EntityTypeName],
                sourceOperators = query.Source?.Operators ?? [],
                terminal = query.ResultKind.ToString(),
                pageConsistency = query.PageConsistency.ToString(),
                cursorOnResults = query.Cursor?.OnResults ?? false,
                resultStages = (query.Pipeline?.Stages ?? []).Select(static stage => new
                {
                    kind = stage.Kind.ToString(),
                    input = stage.InputTypeName,
                    conditional = stage.ConditionSource is not null || stage.FieldSort is { PreserveOrderWhenUnspecified: true }
                }),
                cache = query.Cache.Enabled,
                bypassedVisibility = query.BypassedVisibility ?? [],
                inputValidations = (query.Validations ?? []).Count
            }),
            commands = entity.Commands.OrderBy(static command => command.MethodName, StringComparer.Ordinal).Select(static command => new
            {
                name = command.MethodName,
                kind = command.Kind.ToString(),
                result = command.ReturnTypeName,
                persistence = command.IsAtomic ? "atomicTransaction" : command.IsTracked ? command.SaveChanges ? "save" : "stage" : "setBased",
                conflictColumns = command.ConflictColumns ?? [],
                expectedVersions = (command.ExpectedVersions ?? []).Select(static version => version.PropertyName),
                bypassedVisibility = command.BypassedVisibility ?? [],
                inputValidations = (command.Validations ?? []).Count,
                parameters = command.Parameters.Select(static parameter => new { name = parameter.Name, type = parameter.TypeName })
            })
        })
    }, Options);

    private static string PublicType(string type, bool closed, string infrastructurePrefix) => closed ? ClosedRepositorySourceRenderer.ContractType(type, infrastructurePrefix) : type;
}
