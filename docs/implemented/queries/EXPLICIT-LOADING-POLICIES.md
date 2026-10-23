# Named-query model-loading control

`IgnoreAutoIncludes()` is an optional **design-time** named-query policy. It disables
user-configured EF model `AutoInclude` loading for that operation, without adding a runtime
loading DSL to its service contract. The default is false; existing output stays unchanged.

```csharp
var entity = modelBuilder.Entity<CatalogGroup>();
var detail = entity.HasLoadProfile("Detail", load => load.Include(group => group.Entries));

entity.HasQuery("Cards")
    .IgnoreAutoIncludes()
    .OrderBy(group => group.Id)
    .Select(group => new GroupCard(group.Id, group.Name))
    .List();

entity.HasQuery("Detail")
    .IgnoreAutoIncludes()
    .UseLoadProfile(detail) // Explicit loading is still enabled.
    .Where<int>((group, id) => group.Id == id)
    .ProjectTo<GroupDetail>()
    .SingleOrDefault();
```

Callers invoke `CardsAsync()` or `DetailAsync(id)`. No flag, queryable, persistence entity or
EF policy method appears on a closed application port. Compatibility configured queries
also honor the setting; it does not add a general runtime selector method.

## Scope and independent policies

Both `RepositoryQueryBuilder<TEntity>` and the current `RepositoryResultQueryBuilder<TResult>`
accept the option. After `Select`, `GroupBy` or `From`, it still applies to the **whole query**,
not just the current result stage. A `From` query configures every typed `Set` root; it
does not acquire extra contexts or apply the owning entity's policies to unrelated roots.
Superseded result builders cannot change it. `IgnoreAutoIncludes(false)` restores model loading.

| Policy | Effect when ignoring model automatic loading |
| --- | --- |
| Explicit Load/Include/ThenInclude, entity defaults and named profiles | Retained |
| RepoGen projection AutoIncludeNavigations | Retained; this is a separate inference switch |
| EF global query filters | Retained; only explicit IgnoreQueryFilters bypasses them |
| Designer visibility | Retained; only explicit named BypassVisibility bypasses it |
| Tracking/read-only, splitting, tags, paging, caching and cancellation | Unchanged |
| Owned loading and previously tracked relationship fix-up | Not suppressed |
| Registered query providers | RGD1304; providers own root policies |
| Commands and fresh saved-result queries | Unchanged; a read option does not enable automatic graph writes |

EF may prune unused includes when a DTO projection does not materialize that entity graph.
This option is not a guarantee of zero joins: predicates and projections can still use
relationships. An include alone does not guarantee that a DTO uses its loaded graph. RepoGen
keeps declared expressions; it does not invent client evaluation or rewrite translation failures.
See [EF's loading documentation](https://learn.microsoft.com/en-us/ef/core/querying/related-data/eager#model-configuration-for-auto-including-navigations).

## Optional generated fixtures

Automatic reads still require `UseInMemoryTestQueries()` and explicit stores/maps. With model
loading left enabled, AutoInclude remains a conservative `RequiresOverride` boundary—even for
a particular projection whose provider might prune loading. Fixtures never infer that decision.

With `IgnoreAutoIncludes()` selected, user-configured automatic loading is no longer an active
policy to simulate. Suitable scalar, multi-root and explicitly declared navigation reads run
normally. Accessed relationships and unfiltered profiles still require `HasInMemoryNavigation`
and selected related stores/maps. Snapshots, scripts, pages, cold streams, cancellation and
baseline isolation retain their existing semantics.

Owned eager loading, inheritance, unsupported model filters, filtered includes and other
nonportable shapes remain overrides. Independently opted-in
[portable root model filters](../testing/INMEMORY-MODEL-FILTERS.md) work with loading suppression;
filtered navigation graphs are not enabled. Required explicit CLR
[many-to-many fixture reads](../testing/INMEMORY-MANY-TO-MANY.md) can also suppress model loading, without
enabling direct skip-navigation mutation or changing saved-command coverage.

Only opted-in queries add `ignoreAutoIncludes: true` to the operation manifest. Production source
applies EF's policy; fixtures do not reference EF or invoke a pretend loading operator. Port/API
shapes remain identical when the loading choice changes. Unconfigured artifacts stay byte-stable.

## Focused example and verification

[MemoryLoadingPolicies](../../../samples/ConfigurationExamples/MemoryLoadingPolicies/README.md) maps
[ExplicitLoadingRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryLoadingPolicies/Configuration/ExplicitLoadingRepositoryDesigner.cs)
to exact contracts, production repositories, fixture source and manifest. Its fake-only consumer
uses immutable public seeds with internal persistence models, explicit detail loading, a two-root
read, counted paging, cold streaming and detached graph/baseline checks.

Tests compile all output layouts and both store shapes; verify defaults/toggling, port/API stability,
related filters, owned loading, provider-root restrictions, stale builders and explicit join reads;
and execute generated pipelines against SQLite. Separate production checks materialize an entity
through an explicit top-level graph mapper to distinguish model loading, suppression and retained
explicit loading. Fixture snapshots do not establish universal provider equivalence.
