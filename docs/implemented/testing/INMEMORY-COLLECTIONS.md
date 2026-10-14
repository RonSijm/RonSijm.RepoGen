# Optional in-memory child-collection reconciliation

`SimulateCollectionWrites()` lets a generated test repository execute an existing, explicitly
designed `ReconcileCollection(...)` command against separate parent/child fixture stores. The
service still calls the same named method; it does not assemble a test-specific selection DSL.
This is off by default, independently of ordinary scalar test commands and navigation reads.

Start with [ChecklistWorkspaceRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryCollections/Configuration/ChecklistWorkspaceRepositoryDesigner.cs)
and its [designer → exact output walkthrough](../../../samples/ConfigurationExamples/MemoryCollections/README.md).
The [production reconciliation contract](../writes/COLLECTION-RECONCILIATION.md) remains unchanged.

The same explicit fixture opt-in now supports [ReconcileLinks](../writes/LINK-RECONCILIATION.md) for ordinary
two-FK join rows. It reuses complete target matching, ownership, payload/version rules and shared
Stage/Save without creating/deleting endpoints or simulating database FK constraints. See the
focused [MemoryLinks designer](../../../samples/ConfigurationExamples/MemoryLinks/README.md).
The [MemorySharedLinks designer](../../../samples/ConfigurationExamples/MemorySharedLinks/README.md)
additionally opts into shared FK columns with the same complete-reference-key owner checks as production.

## Configuration

Enable closed test output, selected stores and automatic commands in the model configuration:

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Repositories")
    .UseScriptedTestDoubles("Application.Testing")
    .UseInMemoryTestStores().UseInMemoryTestCommands()
    .RequirePortableTestCommands());
```

Declare each participating store explicitly. Public entity-shaped stores need key/copy adapters.
Internal/private persistence models instead need public storage rows with complete
`MapInMemoryEntity(...)` and `MapInMemoryWrites(...)` scalar maps at both ends. Child repositories
must be selected for test generation and write-enabled even when only the parent command is called.
No reverse DTO mapping, entity constructor behavior or relationship fixup is inferred.

Opt each suitable reconciliation command in separately:

```csharp
entity.HasCommand("SaveItems")
    .Where<ChecklistChange>((row, request) => row.Id == request.Id && row.OwnerId == request.OwnerId)
    .ReconcileCollection<StoredChecklistItem, ChecklistItemInput, string, ChecklistChange>(
        row => row.Items, request => request.Items,
        row => row.Code, item => item.Code,
        children => children.MaximumItems(5)
            .Add(item => new StoredChecklistItem
                { Code = item.Code, Text = item.Text, Version = 1, Hidden = false })
            .Update(setters => setters.SetProperty(row => row.Text, (row, item) => item.Text))
            .AllowWrite(row => row.Version)
            .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
    .Save()
    .SimulateCollectionWrites();
```

The real example also deliberately advances and checks parent/child versions. Adding a version
check does not itself advance a token; the production designer rules still require a suitable
unconditional setter. `SimulateCollectionWrites(false)` turns automatic execution off again.

## Execution contract

| Concern | Fixture behavior |
| --- | --- |
| Input | Materialize and validate once before fixture-partition selection; reject null items/keys, duplicates and excess items. `When(...) == false` skips the item source entirely. |
| Mutation scope | Select the parent using its designed predicates/visibility, then visible saved children whose **complete mapped FK** matches the principal key. Composite ownership is supported. |
| Matching | Default CLR equality for the declared scalar or composite child/item key, not fixture identity comparers or provider collation. |
| Add | Explicit scalar initializer; bind every ownership FK from the selected parent. Other scalars must be explicitly supplied or have explicit insert factories. |
| Update | Approved sequential/conditional scalar setters; cannot change the fixture key, matching key or ownership. |
| Missing | Keep, Reject or tracked Delete exactly as configured. Empty input is distinct from an omitted conditional collection. Hidden/unrelated rows are not missing candidates. |
| Versions | Check parent and supplied child expected values, including structural binary tokens. Deletion of an absent input child uses the loaded original token, not an invented client token. |
| Pending targets | Reject owner-scoped modified/added/deleted children, conservatively including hidden staging. Unchanged entries and unrelated-owner staging can coexist. |
| Stage | Keep owned scalar copies pending; reads still see saved stores. Multiple collections compose with rollback if a later one fails. |
| Save | Prepare all pending stores before committing any. This is a **shared unit-of-work save**, including unrelated pending writes. |
| Failure | Before-save mapping/factory/cancellation/conflict failures restore this command and retain prior unrelated staging. Child conflicts return `Conflict`; unrelated shared-save conflicts throw `RepositoryWriteConflictException`. |
| Results | Saved scalar parent projection, including its explicit save-time generated values. Projection failure after save throws `RepositorySavedResultException`; committed rows remain committed. |
| Rollback/reset | Existing fixture checkpoint rollback and baseline restore apply across parent/child stores. Explicit factories reserve prior pending additions and recompute row-derived IDs after reset. |

Arrange tables separately, not as a seeded navigation graph. Reconciliation neither uses nor
persists navigation membership. Related read results can use separately declared
[navigation snapshots](INMEMORY-PROXY-NAVIGATION.md) or [multi-source queries](INMEMORY-QUERIES.md).
The simple example reads the child repository's already-designed `ListItemsAsync(checklistId)`.

## Coverage and boundaries

The production constraints still apply: direct one-to-many navigation, ordinary keyed leaf
children, a declared unique scalar or [composite matching key](../writes/COMPOSITE-COLLECTION-KEYS.md), explicit allowed writes and bounded membership.
The fixture additionally requires ordinary comparable scalar state, usable ownership keys,
explicit public maps where needed, and explicit factories for generated child insert/update values.
Nested/generic child CLR types excluded from repository generation cannot supply a generated fixture
store; declaring a designer store for such a type does not make it eligible.
Existing generated IDs need no factory for update/delete-only collections.

Active EF filters/AutoInclude, custom scalar/relationship conversion or comparison, shadow keys,
unmapped/non-public state and opaque/domain behavior need typed overrides. Navigation-dependent
ordinary `Returns` still requires a script; explicit [ReturnsSaved](../writes/SAVED-COMMAND-RESULTS.md) can project
fresh related DTOs with the declared fixture navigation-read subset after saving.
Owned/nested/many-to-many graphs, unlink/soft-delete inference, cascade behavior
and provider reload are not broadened by this option. `RequirePortableTestCommands()` reports
uncovered operations with RGD3402; `RequireTestOverride(reason)` acknowledges a scripted operation.
Explicit matching rules/handlers still override generated execution.

Primary fixture-key collisions are rejected. **SQL unique indexes, FK constraints, triggers,
identity sequences, rowversion, locks and concurrent-writer races are not simulated.** A hidden
row's matching key is not probed to fetch/move it; attempting a new row with that key may be rejected
by a real database constraint that the fixture does not model. Use a real-provider fixture for
these guarantees. No one-roundtrip claim is made for either tracked reconciliation or shared save.

Manifests add `collectionSimulation` only to supported opted-in operations, listing child stores,
limits, policies, ownership, pending-target checks and the scalar-only result boundary. Production
contracts, cache dependencies and persistence generation do not change when this test option changes.
