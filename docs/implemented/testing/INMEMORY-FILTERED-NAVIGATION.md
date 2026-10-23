# Optional model filters on fixture navigation reads

`entity.UseInMemoryModelQueryFilter(includeNavigations: true)` lets an explicit fixture graph
reuse that entity's portable scalar filter from the final EF model. The additional permission
is **off by default**. The existing parameterless call still permits row-only roots, not graphs.
Production queries, contracts, API/cache shapes and write permissions are unchanged.

Start with [FilteredShelfRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryFilteredNavigation/Configuration/FilteredShelfRepositoryDesigner.cs)
and [its exact four outputs](../../../samples/ConfigurationExamples/MemoryFilteredNavigation/README.md).
The context defines archive/soft-delete filters once; services call named operations, not a query DSL.

## Configuration and order

```csharp
modelBuilder.Entity<Entry>(entity =>
{
    // After declaring the store/public scalar map:
    entity.UseInMemoryModelQueryFilter(includeNavigations: true);
    entity.HasInMemoryNavigation(entry => entry.Shelf);
    entity.HasQuery("Cards").OrderBy(entry => entry.Id)
        .Select(entry => new EntryCard(entry.Id,
            entry.Shelf == null ? null : entry.Shelf.Id)).List();
});
```

Every filtered participant needs the additional opt-in, including the operation's root. Stores,
public maps, explicit navigation declarations and `UseInMemoryTestQueries()` remain required.
Unfiltered related entities do not need a filter setting. `UseInMemoryModelQueryFilter()` resets
graph permission while retaining row permission; `false` clears both. A disabled backend or missing
filter adds no filter output or receipt.

One detached participating-store snapshot is captured per invocation/stream enumeration. Public
maps run first, then each model filter is applied once to its dataset, then declared relationships
are loaded. Designer root visibility and the normal query pipeline run afterwards. Filters are
not reapplied after loading, and saved rows/seeds/baselines are not removed or changed.

Collections support filtered child counts, membership, aggregates and nested DTOs. Optional
references to filtered targets become null without removing their source rows. Each value access
needs a **local explicit null guard**; an FK value or an earlier query stage does not prove that
the target survived its filter. Conditions/short-circuit Boolean guards are checked per receiver
and expression occurrence. Related identity cannot escape through a DTO/helper. Explicit unfiltered
Include/profile paths can still select these relationships; filtered Include recipes remain separate.

`IgnoreQueryFilters()` bypasses all graph/root model filters. `BypassVisibility` only bypasses
designer visibility. Related repository visibility is never injected into a collection: put any
additional related policy in the designer projection/predicate deliberately.

## Deliberate boundaries

- Required references to filtered targets, in either relationship direction, retain an override.
  Required-navigation queries can remove source rows through joins; merely dropping target rows
  is not a safe substitute. See [EF's required-navigation warning](https://learn.microsoft.com/en-us/ef/core/querying/filters#query-filters-and-required-navigations).
- [Explicit CLR many-to-many graphs](INMEMORY-FILTERED-LINKS.md) now reuse the same graph permission
  on each filtered endpoint/join dataset, with separate required-link and optional-null rules.
  Implicit/shared join graphs retain overrides.
- Unbound context/captured/ambient state, navigation-based filters, inheritance, unbound shadow/unmapped members,
  active AutoInclude and arbitrary provider behavior retain the [existing expression boundaries](INMEMORY-MODEL-FILTERS.md).
  [Explicit key bindings](INMEMORY-MODEL-FILTER-CONTEXTS.md) permit exact context scalars, without
  broadening relationship/null-guard permissions.
  [Explicit shadow read bindings](INMEMORY-SHADOW-READS.md) separately supply model-only scalar
  values from public data; they do not enable filters, shadow relationship keys or tracked state.
- This read permission does not enable filtered graph writes, reconciliation or `ReturnsSaved`.
  [Scalar command filters](INMEMORY-MODEL-WRITE-FILTERS.md) remain a separate opt-in.
  [Fresh saved graphs](INMEMORY-SAVED-FILTERS.md) now additionally require a command-owned graph
  permission; related read flags alone still do not enable them.

Strict coverage reports `RGD3302`; acknowledged operations use normal typed scripts. Successful
receipts identify predicates, every graph opt-in, application order and bypass scope. The backend
is still LINQ-to-Objects, not a SQL null/collation/tracking emulator. SQLite contract cases compare
the actual generated operations for both public entity and mapped-data fixture stores.
