# Optional model filters on explicit many-to-many fixture reads

`UseInMemoryModelQueryFilter(includeNavigations: true)` also permits portable scalar model
filters on explicitly declared CLR many-to-many graphs. The setting remains **off by default**,
independent on each filtered participant and separate from command permissions. No new CLI flag,
production query rewrite, service-side query DSL or default dependency is added.

Start with [FilteredCatalogRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryFilteredLinks/Configuration/FilteredCatalogRepositoryDesigner.cs)
and [its exact four outputs](../../../samples/ConfigurationExamples/MemoryFilteredLinks/README.md).
The model owns the filters; the designer chooses whether fixture operations may reuse them.

## Which declarations are needed?

```csharp
modelBuilder.Entity<Article>(entity =>
{
    // First declare HasInMemoryStore and any public scalar map.
    entity.UseInMemoryModelQueryFilter(includeNavigations: true);
    entity.HasInMemoryNavigation(article => article.Labels);
    entity.HasQuery("Cards").OrderBy(article => article.Id)
        .Select(article => new ArticleCard(article.Id,
            article.Labels.OrderBy(label => label.Id).Select(label => label.Name).ToList()))
        .List();
});

// If these model datasets have filters, each needs the same graph permission:
modelBuilder.Entity<Label>(entity => entity.UseInMemoryModelQueryFilter(includeNavigations: true));
modelBuilder.Entity<ArticleLabel>(entity => entity.UseInMemoryModelQueryFilter(includeNavigations: true));
```

All three stores must be selected and any public read rows explicitly mapped. The final EF model
must describe an ordinary CLR join entity, both foreign/principal key sets and inverse metadata.
Only accessed/loaded edges are rebuilt; an accessed inverse needs its own declaration. Join-only
participants do not need repository methods. Existing query-backend, map, key, load-path and
translation checks still apply. A row-only permission is not graph consent.

## What is filtered, and when?

One detached participating-store snapshot is captured per invocation or cold-stream enumeration.
Rows are mapped once. Each permitted model filter runs once on its own dataset before loading
the declared edges. Complete unfiltered endpoint keys are retained for fixture validation; model
filtering must not turn a legitimate hidden principal into a missing-required-row failure.

| Dataset/policy | Effect |
| --- | --- |
| Filtered source endpoint | That source is absent from the read dataset; links to it do not create an owner |
| Filtered join row | Does not participate in this edge, including its pair/required-reference checks |
| Filtered required target | Its link is omitted; the surviving owner remains, possibly with an empty collection |
| Filtered optional target | Uses the existing explicit target-null policy described below |
| Designer visibility | Applies only to declared query roots, not automatically to related endpoints/join rows |
| IgnoreQueryFilters | Bypasses all participating model filters, not designer visibility |
| BypassVisibility | Bypasses only the named designer policies, not model filters |

Principal keys must be unique across the unfiltered captured endpoint rows. Visible join pairs
must be unique, including pairs whose source/target was filtered out. A truly absent required
principal or null required FK still fails before edge assignment. Explicit relationship component
comparers apply to both visible matching and unfiltered-key validation. Fixture dictionary keys,
ordinary query equality and write matching are unchanged. Loading preserves join snapshot order;
use an explicit related sort when result order matters.

These validity checks are deliberate fixture safeguards, not provider query validation. Database
constraints, collation, races, tracking and SQL translation remain real-provider responsibilities.
[EF's filter documentation](https://learn.microsoft.com/en-us/ef/core/querying/filters) describes
global filters and the separate required-reference caveat; RepoGen keeps that ordinary-reference
boundary instead of inferring root pruning from this collection feature.

## Optional targets still need an explicit null policy

An optional join FK can leave a missing target even when the FK value is non-null: the target's
model filter may hide it. The existing two policies are unchanged:

- By default, every use begins with `Where(target => target != null)` before count, sort,
  membership or projection. The fixture omits missing/filtered targets under that explicit guard.
- `HasInMemoryNavigation(row => row.Labels, preserveMissingTargets: true)` permits supported
  null-safe scalar/DTO pipelines. Each missing/filtered target retains a null slot per visible
  join row. Local null guards remain required for member access; explicit loading and entity
  identity escape are not enabled.

See [guarded optional links](INMEMORY-OPTIONAL-LINKS.md) and [null-preserving reads](INMEMORY-NULL-TARGETS.md).
Required targets never acquire null slots. Seeds, saved rows, baselines and pending changes remain
intact; a detached collection is not a persisted graph.

## Fresh saved projections have their own permission

Scalar commands/batches using navigation-based `ReturnsSaved` additionally need
`command.UseInMemoryModelQueryFilter(includeNavigations: true)`. Related filtered datasets still
need their entity-level graph opt-in. Root read permission does not grant command consent.

The shared Save happens first, followed by one fresh coordinated filtered result snapshot.
Single and bounded batch results retain complete saved keys and input order. Invalid/missing
saved results fail with `RepositorySavedResultException` after saving; they never silently shorten
a batch or undo persistence. Use an explicit transaction/checkpoint when rollback is required.
This adds read projections after scalar writes, not filtered reconciliation or graph mutation.
See [filtered saved results](INMEMORY-SAVED-FILTERS.md).

## Receipts, testing and boundaries

`modelQueryFilters`/`savedModelQueryFilters` retain the accepted predicates and permissions.
Each affected `navigationSnapshot.relationships[].join` or `savedResultQuery.joinRelationships[]`
additionally records which source/target/join filters apply, omitted required links, optional-null
policy and unfiltered-principal/visible-pair validity. Unfiltered and disabled output is unchanged.
Typed scripts still override supported operations; unsupported unarranged calls fail even on
empty stores. Strict coverage uses the existing RGD3302/RGD3402 diagnostics.

Explicit context-scalar [typed-key bindings](INMEMORY-MODEL-FILTER-CONTEXTS.md) and permitted helper
translations remain independent opt-ins. Implicit/shared/owned/inherited joins, unmapped/shadow
keys, unbound captures, active AutoInclude, filtered Include recipes, unsafe nullable expressions
and arbitrary provider/mapper behavior retain overrides. Required ordinary references to filtered
targets are not covered by a many-to-many collection rule.

Tests compare actual generated SQLite repositories and both entity-shaped/public-data fixtures:
endpoint/join filters, composite alternate keys, inverse/multi-edge reads, membership, root bypasses,
From roots, pages, optional null slots and single/batch saved DTOs. Runtime tests separately cover
invalid/duplicate hidden keys, explicit comparison and cancellation. This is bounded contract
coverage, not universal SQL equivalence.
