# Explicit mapped scalar state in fixture collections

Complete separately opted-in [mapped scalar write state](INMEMORY-MAPPED-WRITES.md) can participate
in existing bounded ReconcileCollection and explicit ReconcileLinks simulations. A parent, a child,
or both can use exact shadow, field-backed, field-only, indexer or ordinary scalar bindings.
No new global setting, CLI option, service-side query DSL or relationship-key permission is added.

Start with [RevisionChecklistRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryMappedCollections/Configuration/RevisionChecklistRepositoryDesigner.cs)
and its [exact designer → output map](../../../samples/ConfigurationExamples/MemoryMappedCollections/README.md).
Its two tables have separate shadow Revision tokens and deliberately configured generated-value
recipes. Services use named application ports and public DTOs, not EF or mutable entity graphs.

## Independent configuration

Configure every participating writable fixture store explicitly. Each mapped participant needs:

- A public HasInMemoryStore and complete MapInMemoryEntity scalar initializer/read bindings.
- MapInMemoryWrites(..., useMappedPropertyState: true), with a complete structural round trip.
- Exact generated-value factories where its final model requires create/update generation.

Then opt the designed command into SimulateCollectionWrites. A read binding or write-state flag
alone does not enable collection execution. Ordinary public/entity-shaped parents or children can
coexist with mapped participants; an ordinary parent does not need dummy mapped values.

```csharp
child.HasInMemoryStore<ItemSeed, int>(row => row.Id,
        row => new(row.Id, row.ChecklistId, row.Code, row.Text, row.Counter, row.Revision))
    .MapInMemoryEntity<ItemSeed>(row => new StoredItem
        { Id = row.Id, ChecklistId = row.ChecklistId, Code = row.Code, Text = row.Text, Counter = row.Counter })
    .MapInMemoryProperty<ItemSeed, int>("Revision", row => row.Revision)
    .MapInMemoryWrites<ItemSeed>((row, saved) => new(row.Id, row.ChecklistId, row.Code, row.Text,
        row.Counter, EF.Property<int>(row, "Revision")), useMappedPropertyState: true)
    .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Counter + 1);

parent.HasCommand("SaveItems")
    .Where<ChecklistChange>((row, request) => row.Id == request.Id)
    .ReconcileCollection<StoredItem, ItemChange, string, ChecklistChange>(
        row => row.Items, request => request.Items, row => row.Code, item => item.Code,
        items => items.MaximumItems(10).AllowWrite(row => row.Id)
            .Add(item => new StoredItem { Id = item.Id, Code = item.Code, Text = item.Text, Counter = 0 })
            .Update(setters => setters.SetProperty(row => row.Text, (row, item) => item.Text)
                .SetProperty(row => row.Counter, row => row.Counter + 1))
            .ExpectVersion<int>("Revision", item => item.ExpectedRevision)
            .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
    .SimulateCollectionWrites().Save();
```

The example's parent additionally has its own complete map, counter setter and original-version
check. Factories are fixture recipes, not inference of SQL expressions, rowversions or sequences.
Names and CLR types must match final-model properties exactly. A scalar binding alone does not
create a private/shadow setter. Separate [named reconciliation setters](../writes/MAPPED-RECONCILIATION-SETTERS.md)
explicitly assign approved mapped parent/child scalars, with separate per-collection typed permissions
and complete independent state. See [MappedCollectionSetters](../../../samples/ConfigurationExamples/MappedCollectionSetters/README.md).

## State and execution boundaries

Only the root and explicitly reconciled, selected writable children grant command scalar bindings.
Bindings remain keyed by entity type, so identically named parent/child tokens cannot share a slot
accidentally. Read-only targets, unrelated stores and read/navigation flags do not authorize writes.
Generated fixture delegates use typed slots, not entity constructors/getters/indexers, EF.Property
calls or reflection-based mutation.

| Concern | Behavior |
| --- | --- |
| Identity and matching | Ordinary complete primary/unique/matching/ownership scalars remain required; named state cannot replace them |
| Add | Explicit scalar initializer, ownership bound from the selected parent, and declared factories/default rules |
| Update | Existing approved sequential/conditional setters, routed to their exact scalar slots |
| Versions | Explicit scalar/nullable/binary checks on loaded originals; shared Save rechecks original state |
| Missing children | Keep/Reject/Delete only within existing owner, visibility and independently permitted model-filter scope |
| Stage | Saved stores remain unchanged; pending originals include mapped tokens |
| Save | Prepare factories, copies and full structural read → write → read validation for every pending store before any commit |
| Failure | Pre-save failures restore this command and preserve prior unrelated staging; original-version conflicts remain explicit |
| Rollback | Existing checkpoints/baselines restore all mapped participant stores |

Owner-scoped modified/added/deleted children still block reconciliation, including hidden/filtered
ones. Unrelated-owner staging can coexist, and a saving command still saves the shared unit of work.
Unchanged returned rows and fixture copy recipes retain their existing detachment rules; copy
recipes must copy mutable values. A lossy encoder is not excused by custom tracked equality.

## Results, filters and explicit links

Use ReturnsSaved for shadow values or fresh related DTOs: EF.Property is valid in the saved LINQ
query, not in a client-side ordinary Returns delegate. Automatic fixture reads remain independent
for scalar writes/results. Saved navigation results still require their own declared navigation/read
permissions and explicit projection-owned filtering; child repository visibility is not an implicit
filter on every nested projection.

Root model filters still need per-command UseInMemoryModelQueryFilter. Child/link filters separately
need SimulateCollectionWrites(includeModelQueryFilters: true). Mapped values and entity read flags do
not grant either permission. Filters constrain selection/missing candidates, not insertion or shared
Save. Existing AutoInclude, context-binding, null and filtered-graph guards remain.

Explicit link entities can use non-identity mapped state too. Complete ordinary FK/reference keys,
shared-reference guards, creation-only additional references, immutable matched references, bounded
membership and endpoint isolation are unchanged. This does not enable implicit/shared join entities,
skip-navigation mutation, owned/nested graphs, cascades or automatic aggregate persistence.

## Coverage and limitations

Supported operations report Generated and collectionSimulation.mappedScalarState: each permitted
parent/child binding, ordinary identity, original-version checks, explicit factories, full structural
round trips and providerEquivalence: false. Existing scalar/collection receipts remain available.
Unconfigured output is unchanged. Invalid/incomplete maps retain RGD3601; unsupported operations
retain RGD3402, explicit override acknowledgement and strict failures even for empty stores.
Scripts/handlers still take precedence.

Production contracts, API/cache shapes, SQL and writer/relationship validations do not change.
These are service-test semantics, not SQL constraints, FK/unique enforcement, triggers, generated
identity, database rowversion, durability, locks or race guarantees. Actual generated SQLite cases
exercise ordinary bounded reconciliation, named versions, fresh results and filter selection; use
real-provider fixtures for provider guarantees.

[Collection simulations](INMEMORY-COLLECTIONS.md) · [Mapped write state](INMEMORY-MAPPED-WRITES.md) ·
[Filtered collection writes](INMEMORY-COLLECTION-FILTERS.md) · [Testing index](README.md)
