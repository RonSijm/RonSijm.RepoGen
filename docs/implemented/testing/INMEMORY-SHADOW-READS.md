# Explicit shadow values in fixture reads

`MapInMemoryProperty<TData, TProperty>(name, value)` supplies one exact final-model shadow
scalar from public fixture data. It is optional, read-only adaptation: ordinary/simple generation
and production query translation are unchanged. Tests can use the same named application ports
without referencing EF, entities, contexts, designers or generator tooling.

This is the shadow-focused walkthrough of the unified
[MapInMemoryProperty API](INMEMORY-MAPPED-READS.md). The earlier shadow-only method name has been
replaced; the same optional API also covers fields, indexers and ordinary mapped properties.

## Configure in the RepositoryDesigner

After declaring the matching public store and ordinary scalar read map:

```csharp
entity.HasInMemoryStore<NoteSeed, int>(row => row.Id,
        row => new(row.Id, row.Text, row.Rank, row.Deleted))
    .MapInMemoryEntity<NoteSeed>(row => new StoredNote { Id = row.Id, Text = row.Text })
    .MapInMemoryProperty<NoteSeed, int>("Rank", row => row.Rank)
    .MapInMemoryProperty<NoteSeed, bool>("Deleted", row => row.Deleted);

entity.UseInMemoryModelQueryFilter(); // Separate permission, if the context has a scalar filter.
entity.HasQuery("Ranked")
    .Where<int>((row, minimumRank) => EF.Property<int>(row, "Rank") >= minimumRank)
    .OrderBy(row => EF.Property<int>(row, "Rank"))
    .ThenBy(row => row.Id)
    .Select(row => new NoteCard(row.Id, row.Text, EF.Property<int>(row, "Rank")))
    .List();
```

Generation requires the existing closed/scripted/store/read settings. `name` is trusted designer
metadata, not a service argument. The application still calls `RankedAsync(minimumRank)`.
Production keeps EF.Property and the provider's translation; only the fixture lowering changes.

## Supported behavior

- For this shadow-focused case, choose a shadow scalar in the final EF model with the exact value type.
  Public primitive/enum/string/decimal/Guid/date/time scalars, nullable forms and byte arrays are
  supported. A converted enum uses its model enum type, not its provider string type.
- Value recipes are self-contained portable scalar expressions over the declared public data row.
  Ordinary mapping guards reject captured services, clocks, arbitrary helpers, private/model types
  and invoked delegates. Generation never compiles/invokes these recipes or evaluates getters.
- Exact constant-name, exact-type EF.Property reads become typed internal read-row members. No
  runtime reflection, dictionary property lookup or EF helper is emitted. Quoted/unusual metadata
  names never become identifiers; generated slot names avoid CLR/navigation member collisions.
- Named predicates, projections, default/explicit ordering, groups, aggregates, dictionaries,
  pages, cold streams and explicit From roots can reuse the values within their existing portable
  expression/terminal boundaries. This is not a new SQL-translation guarantee for an expression.
- Separately opted-in final-model scalar filters reuse bound shadow values before designer
  visibility and query pipelines. `IgnoreQueryFilters` bypasses only model filters, not designer
  visibility. Missing filter permission still requires an override; binary result bindings do not
  broaden the existing model-filter type/operator subset.
- Existing declared navigation graphs can read bound values from related mapped read rows.
  Every dataset supplies its own bindings/filter permission. Filtered optional references still
  need local null guards, including around EF.Property reads on the related row. Required filtered
  references retain their existing override boundaries. Shadow relationship keys additionally
  need separate [per-edge permission](INMEMORY-MAPPED-KEYS.md); scalar bindings alone do not grant it.

The public fixture store owns arrangement, copying, baselines and reset. Mutable values such as
byte arrays must be deep-copied by its explicit copy adapter. Each invocation/enumeration uses
detached snapshots; filtering never deletes arrangement rows. Scripts retain precedence.

## Configuration lifetime and diagnostics

Redeclaring the same exact name replaces its value recipe. `WithoutInMemoryProperty(name)`
removes one binding. Replacing/removing the store or calling `WithoutInMemoryEntityMapping()`
also removes its bindings. Removing a still-used binding restores an override boundary; it never
returns `default(T)` or an empty success result, even on empty stores.

`RGD4901` rejects missing/mismatched public maps, unknown properties, duplicate initializer/named
scalar sources, wrong value types and nonportable recipes. With `RequirePortableTestQueries`, unbound, dynamic-name,
boxed/widened-type or otherwise unsafe reads retain `RGD3302` unless acknowledged through
`RequireTestOverride(reason)`. Non-strict unsupported invocations throw until a matching typed
script/handler is supplied. Disabling the read backend removes the feature from generated output.

Manifests add `readMapping.propertyMappings` only when selected: exact names/types, generated
slots and recipes, with `DetachedReadRowsOnly`, no tracked-state or provider-equivalence claim.
Per-operation `propertyReads` receipts list only consumed source/property/type bindings;
ignoring a model filter removes query-expression reads contributed only by that filter. Like ordinary
public read maps, the detached adapter still populates every configured slot; this is not column
pruning or a promise to skip unused value recipes.
Production implementations, public contracts, API snapshots and ordinary defaults are unchanged.

## Deliberately not tracked shadow-state simulation

This permission does not enable shadow writes/setters, original concurrency-token state or token
advancement, provider defaults/computed values, shadow relationship/key identity, change tracking,
native returned buffers or saved command results. Those operations still need their own supported
configuration, typed overrides or real-provider fixtures. Field/indexer/CLR properties can use the
same [explicit scalar API](INMEMORY-MAPPED-READS.md), without pretending they are shadow properties.

See [MemoryShadowReads](../../../samples/ConfigurationExamples/MemoryShadowReads/README.md) for
the focused designer, four exact outputs and EF-free consumer. Regression cases compare actual
generated SQLite and fixture operations, including converted/null/binary values, grouped reports,
From joins, counted pages and filtered collection/guarded optional-reference reads.

[Public data mappings](INMEMORY-PROXIES.md) · [Model filters](INMEMORY-MODEL-FILTERS.md) ·
[Production mapped bindings](../repositories/MAPPED-PROPERTY-BINDINGS.md) · [Testing index](README.md)
