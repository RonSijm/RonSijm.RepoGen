# Optional navigation-aware fixture reads

`HasInMemoryNavigation` opts one mapped reference or collection into the portable test-read
backend. Production still uses the original designer query and EF relationship. A service calls
the same named DTO operation; it does not build a separate fake graph or navigation DSL.

Start with [NavigationCatalogRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryNavigation/Configuration/NavigationCatalogRepositoryDesigner.cs)
and its [designer → output map](../../../samples/ConfigurationExamples/MemoryNavigation/README.md).
This extends [portable reads](INMEMORY-QUERIES.md), not fixture arrangement or fake persistence.

Separate [mapped relationship-key permission](INMEMORY-MAPPED-KEYS.md) allows exact scalar bindings
to supply shadow/field/indexer components for one edge. Ordinary declarations/defaults below do
not infer those values or enable write state.

## Declare the relationships, not a second key mapping

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Generated")
    .UseScriptedTestDoubles("Tests.Generated")
    .UseInMemoryTestStores().UseInMemoryTestQueries());

modelBuilder.Entity<CatalogGroup>(entity =>
{
    entity.WriteEnabled = false;
    entity.HasInMemoryStore(group => group.Id, group => new CatalogGroup
    {
        Id = group.Id, Code = group.Code, Region = group.Region, Name = group.Name
    });
    entity.HasInMemoryNavigation(group => group.Entries);
    entity.HasQuery("Names").OrderBy(group => group.Id)
        .Select(group => group.Entries.OrderBy(entry => entry.Id)
            .Select(entry => entry.Category == null ? null : entry.Category.Name).ToList())
        .List();
});

modelBuilder.Entity<CatalogEntry>(entity =>
{
    entity.WriteEnabled = false;
    entity.HasInMemoryStore(entry => entry.Id, entry => new CatalogEntry
    {
        Id = entry.Id, GroupCode = entry.GroupCode, GroupRegion = entry.GroupRegion,
        CategoryId = entry.CategoryId
    });
    entity.HasInMemoryNavigation(entry => entry.Category);
});

modelBuilder.Entity<CatalogCategory>(entity =>
{
    entity.WriteEnabled = false;
    entity.HasInMemoryStore(category => category.Id,
        category => new CatalogCategory { Id = category.Id, Name = category.Name });
});
```

The final EF model supplies the foreign/principal key pair, its direction, cardinality and
requiredness. Composite and alternate principal keys retain their declared component order.
Those keys need not be the fixture store's key: a store may be indexed by Id while a relationship
joins Code + Region. No relationship is inferred from property names or seeded object pointers.

Declare nested relationships on the builder for their declaring entity. A direct selector is
required; a dotted path, method call or scalar property is invalid (RGD3304). Declaring the same
member again is idempotent. `WithoutInMemoryNavigation(group => group.Entries)` removes that
test-only declaration. It does not change production includes, keys, projections or scripts.

All settings remain optional. Without navigation declarations the previous navigation override
boundary remains unchanged. Disabling `UseInMemoryTestQueries` ignores these test-read declarations.
Simple users get no new output, dependency, constructor parameter or runtime API.

## What happens on a read

The generator inventories navigation access in predicates, visibility/default sorting, projections,
groups, result stages, cursors, explicit From roots and resolved unfiltered includes/load profiles.
It captures all syntactically participating stores once under one fixture partition's lock. Copies
finish before loading/query execution; unused stores are not copied. Self-references share one table
snapshot. Store-only participants do not need their own query methods.

Generated typed code uses ordinary CLR property assignments and indexed relationship lookups:

- A reference resolves to its unique target, or null when optional and unmatched.
- A collection is replaced with a new List of matching rows in snapshot order; empty means empty.
- Null in **any** key component never joins. Default equality is ordinal CLR/structural equality,
  including array components. Optional [principal-component comparison](INMEMORY-RELATIONSHIP-COMPARERS.md)
  is explicitly configured, not inferred database collation or EF comparison.
- Duplicate reference-target or collection-owner keys fail clearly. A missing required dependent
  reference also fails, identifying the relationship without exposing key values. These checks
  cover the captured rows, before root filtering; fixtures must arrange valid required relationships.
- Only accessed/loaded, explicitly declared edges are rebuilt. A declared but unused inverse is
  not automatically populated. Explicit inverse/self-reference paths can share detached row identity
  within that invocation, without recursive loading or cross-call tracking.

Loading does not change arranged rows, store dictionaries, baselines or later calls. Mutable values
still require the explicit deep-copy adapter. Entity-shaped rows and their public property
getters/setters must be suitable for ordinary test loading; RepoGen does not neutralize entity
business behavior. Production closed contracts still reject persistence entities as results: return
DTOs/scalars even though the testing fixture stores deliberate public entity-shaped arrangement data.

Pages keep one loaded graph for count/items. Cold streams load on their first move and retain that
detached graph until enumeration ends. Method/enumeration cancellation checks and existing scripts,
recording/reset and keyed fixtures continue to apply. A failed load never changes fixture data;
assignments already made on an abandoned detached graph are not a fixture transaction.

## Navigation visibility is not root visibility

RepoGen visibility policies apply to an actual repository/From root. They are **not** automatically
injected into relationships. This matches production: `group.Entries.Count` does not implicitly
apply an Entry repository's `!entry.Hidden` root policy. Put the intended child predicate inside
the shared designer projection/filter, or explicitly review deliberately unfiltered access with
[navigation visibility receipts](../queries/NAVIGATION-VISIBILITY.md).

An active EF global filter on any participating entity still requires an override. An operation's
explicit `IgnoreQueryFilters` removes that boundary, but does not remove unrelated boundaries.
Active AutoInclude and inheritance remain unsupported. A named-query
[IgnoreAutoIncludes policy](../queries/EXPLICIT-LOADING-POLICIES.md) disables user-configured model loading
in production and removes only that fixture boundary. Explicit paths/projections still need
declared relationships; owned loading and other policies retain overrides.
A review reason is not a filter, authorization
implementation or proof of SQL parity.

Unfiltered Include/ThenInclude chains and named load profiles can identify edges. Filtered includes
are separate from [opted-in scalar model filters before loading](INMEMORY-FILTERED-NAVIGATION.md).
Those support ordinary collections and locally guarded optional references, plus separately
declared [explicit CLR many-to-many datasets](INMEMORY-FILTERED-LINKS.md). Required filtered
ordinary references and implicit/shared joins stay scripted. Filtered includes themselves
remain RequiresOverride: graph-loading filters must not be silently applied to a DTO projection
that EF translates independently of Include. The generator does not simulate split/single SQL
strategy, identity-resolution tracking or ambient previously tracked entities.

## Supported boundary and receipts

Selected **entity-shaped** stores retain their ordinary public readable/writable non-init navigation
requirements. Collections must accept List<T>; relationship keys need public readable CLR scalars.
Selected **public data-shape** stores can instead declare scalar maps at both ends and load generated
plain relationship slots, without persistence construction/navigation setters. Read-only/private-setter
model navigations can be described this way; their domain/custom-collection behavior is not executed.
See [mapped navigation semantics](INMEMORY-PROXY-NAVIGATION.md) and the focused
[MemoryProxyNavigation designer](../../../samples/ConfigurationExamples/MemoryProxyNavigation/README.md).

Missing mapped scalars/keys, tracked/unbound shadow state, custom converted/provider-compared or unacknowledged CLR keys, unadapted read-only/custom
entity-shaped graphs, owned/shared/inherited
entities require typed scripts or real-provider fixtures. Explicitly declared many-to-many edges
with an ordinary CLR join store and required foreign keys are separately supported; see
[many-to-many loading](INMEMORY-MANY-TO-MANY.md). [Nullable-link reads](INMEMORY-OPTIONAL-LINKS.md)
separately support optional source keys and explicitly guarded targets. Implicit/shared joins and
unguarded target null propagation/loading remain overrides.
Explicit From joins are also available; opting into one navigation does not imply all EF graph features.

[Exact shadow read bindings](INMEMORY-SHADOW-READS.md) can supply model-only scalar values from public
data on declared read rows. This does not invent shadow relationship identity or tracked state.

Unsupported reads keep deterministic RequiresOverride reasons (RGD3302 in strict mode unless
`RequireTestOverride(reason)` acknowledges them), even when all stores are empty. Script controls
still override a supported generated read. Per-operation `navigationSnapshot` receipts list the
participating stores, named edges, direction-specific key expressions and required/collection flags,
explicitly reporting no implicit related policy injection or write adaptation. No test-only
relationship information enters production/API/cache query fingerprints.

Shared tooling contract cases compare nested projections, collection filters/aggregates, references,
optional/composite keys, pages and unfiltered loading with real generated SQLite repositories.
They are not a universal SQL-provider or transaction/concurrency guarantee. Use the
[real-provider contract fixture](REPOSITORY-CONTRACT-FIXTURES.md) for those concerns.

Separately opt-in [tracked scalar writes](INMEMORY-WRITES.md) support Stage/shared Save and checkpoint
rollback. [Explicit public scalar maps and navigation slots](INMEMORY-PROXY-NAVIGATION.md) also
support suitable internal/read-only model reads. [Public scalar write adapters](INMEMORY-PROXY-WRITES.md)
are separately available; graph reconciliation remains
separate roadmap extensions. Navigation loading does not insert, reconcile or persist a related graph.
