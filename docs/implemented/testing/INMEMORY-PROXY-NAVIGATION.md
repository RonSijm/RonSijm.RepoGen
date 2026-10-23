# Explicit navigation loading for public fixture shapes

[Public scalar mappings](INMEMORY-PROXIES.md) and [declared navigation reads](INMEMORY-NAVIGATION.md)
can be combined. Private/internal persistence entities need not be constructed or exposed to an
EF-free test consumer. Configuration stays in the RepositoryDesigner; the service calls the same
named DTO operation against production and its optional test double.

See [ProxyShelfRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryProxyNavigation/Configuration/ProxyShelfRepositoryDesigner.cs)
and its [exact declaration-to-output example](../../../samples/ConfigurationExamples/MemoryProxyNavigation/README.md).

For shadow/field/indexer relationship components, opt in separately per edge with
[useMappedPropertyKeys](INMEMORY-MAPPED-KEYS.md). Scalar read bindings alone do not grant that permission.

## Explicit scalar maps plus explicit relationships

```csharp
modelBuilder.Entity<ShelfRecord>(entity =>
{
    entity.WriteEnabled = false;
    entity.HasInMemoryStore<ShelfSeed, int>(seed => seed.Key,
            seed => new ShelfSeed(seed.Key, seed.Code, seed.Region, seed.Caption, seed.Hidden))
        .MapInMemoryEntity<ShelfSeed>(seed => new ShelfRecord
        { Id = seed.Key, Code = seed.Code, Region = seed.Region, Name = seed.Caption, Hidden = seed.Hidden });
    entity.HasInMemoryNavigation(row => row.Items);
    entity.HasQuery("Names").Select(row => row.Items.OrderBy(item => item.Id)
        .Select(item => item.Category == null ? null : item.Category.Name).ToList()).List();
});
```

Declare matching public stores/scalar maps for ShelfItemRecord and ShelfCategoryRecord, and
`HasInMemoryNavigation(item => item.Category)` on the item builder. The EF model still declares
foreign/principal keys, cardinality and requiredness. No association or reverse map is guessed from
member names, row keys, DTO projections or supplied object pointers.

`MapInMemoryEntity` still permits only scalar initializer assignments. Do **not** assign navigations
inside that initializer. `HasInMemoryNavigation` independently creates a plain read-row slot and
opts that edge into key-based loading. Original entity constructors, property initializers, getters,
navigation setters and custom collection instances are not used. Public seed constructors and
copy/mapping expressions remain consumer-owned pure test adapters.

## Snapshot and loading order

1. Capture detached copies of all syntactically participating stores under one partition's lock.
2. Map each public store's rows once to its generated scalar read rows, with cancellation checks.
3. Load only accessed/unfiltered-included and explicitly declared edges into those same rows.
4. Apply repository/From-root visibility and execute the original scalar/DTO pipeline.

Collection slots use new List<TReadRow> instances. Optional references may be null; required
dependent references must resolve or fail clearly. Composite/alternate keys preserve their mapped
component order; null components never join. Duplicate target/owner relationship keys fail without
changing arranged data. Self-references reuse one snapshot, without recursive entity loading.

All relationship key members must be explicitly assigned on mapped sources/targets, even if a
projection does not mention them. Every scalar accessed on a related mapped row must also be assigned.
Omitting a key/member does not produce a default, even for an empty store: it is RequiresOverride
(RGD3302 in strict mode unless explicitly acknowledged with `RequireTestOverride`).

Read-only/private-setter model navigations can be described because assignments target generated
plain slots, not the model. This does not simulate their domain setters or custom collection behavior.
Returning persistence entities/identity through a projection or using model methods remains unsupported;
return DTOs/scalars, including from nested collections. Unfiltered Include/ThenInclude/load profiles
name edges; those selectors do not return entity identity to the application.

## Policies, ownership and optional behavior

Related repository visibility is not injected into an edge. Put child filters in the shared designer
projection, or review deliberately unfiltered access with [navigation visibility](../queries/NAVIGATION-VISIBILITY.md).
Policies on actual From roots are applied after loading and before source composition. A reviewed
navigation is not an authorization implementation or evidence of SQL parity.

Pages retain one graph for count/items. Cold streams map/load at first move and retain that graph
until enumeration ends; a later enumeration gets a new snapshot. Returned DTO mutation, load failures,
cancellation and early disposal never mutate arrangement rows/baselines. Typed scripts still override
automatic reads; script reset does not clear fixture data. All participating rows use the selected
context partition. Mapping runs outside the fixture lock, over detached copies.

There is no new global setting: existing optional stores, automatic reads, scalar mappings and
relationship declarations enable the combination. Removing a map/declaration or disabling automatic
reads retains scripts and ordinary production/contracts/API/cache shapes. Declared but unused edges
are not loaded automatically. Replacing/removing a store clears its scalar map; navigation declarations
remain separately configurable and must still match actual model members.

## Conservative boundaries

- Entity-shaped sources cannot receive mapped-target read rows in their original typed navigation.
  Map both ends, or use an explicit script. A mapped source can reference a suitable public entity-shaped
  target; that explicitly selected target then requires its model type at runtime.
- Unpermitted/unbound shadow/field/indexer keys, missing mapped keys, custom EF key conversions/comparers, ownership/inheritance,
  global EF filters, AutoInclude, filtered includes, implicit/shared joins and unguarded nullable target loading retain existing
  override/real-provider boundaries. Mapped relationship keys must be explicitly represented by
  suitable scalar assignments; shadow state is not invented.
- Merely declaring a mapped target does not select unsupported owned/shared/nested/generic model
  types for fixture output. No dangling target-row slot is emitted; dependent reads require an
  override, while otherwise portable scalar operations retain their own coverage.
- No public-proxy reverse mapping, generated proxy values, graph/native writes, tracker fixup,
  database constraints/collation/isolation or cross-process races are inferred.

Explicit CLR join entities with selected scalar-mapped stores and required foreign keys can load
declared many-to-many read slots. All three tables are captured together; no graph write adaptation
is inferred. See [many-to-many loading](INMEMORY-MANY-TO-MANY.md) and
[MemoryManyToMany](../../../samples/ConfigurationExamples/MemoryManyToMany/README.md).
[Nullable join links](INMEMORY-OPTIONAL-LINKS.md) additionally support optional source FKs and
explicit leading non-null target filters in the shared designer query, with the same scalar maps.

[Explicit scalar/write adapters](INMEMORY-PROXY-WRITES.md) can separately support public rows,
generated-value factories and Stage/Save. They do not persist the navigation slots or turn this
read-loading feature into graph reconciliation.

Manifest `readMapping.navigationSlots` receipts name the plain slot/type/cardinality/requiredness;
the scalar mapping reports `constructsPersistenceEntity: false` and `writes: NotAdapted`.
Operation `navigationSnapshot` receipts identify only actual participants and mapped keys, explicitly
reporting no implicit related policy injection or write adaptation.
Tooling tests compile private mapped graphs without even a model-assembly reference and compare shared
named operations against actual generated SQLite repositories. Use [real-provider fixtures](REPOSITORY-CONTRACT-FIXTURES.md)
for provider/concurrency claims; these scalar/graph checks are not universal SQL equivalence.
