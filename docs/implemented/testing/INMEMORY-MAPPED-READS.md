# Explicit mapped scalar reads in fixtures

`MapInMemoryProperty<TData, TProperty>(name, value)` supplies one exact final-model scalar from a
public fixture row. It covers shadow, field-backed, field-only, indexer and ordinary CLR properties
through one optional designer API. Production query translation/contracts remain unchanged.
The fixture reads column-like values, not entity constructors, getters or indexer implementations.

## Configure once in the RepositoryDesigner

```csharp
entity.HasInMemoryStore<MemoSeed, int>(row => row.Id,
        row => new(row.Id, row.Caption, row.Revision, row.Source))
    .MapInMemoryEntity<MemoSeed>(row => new StoredMemo { Id = row.Id })
    .MapInMemoryProperty<MemoSeed, string>(nameof(StoredMemo.Caption), row => row.Caption)
    .MapInMemoryProperty<MemoSeed, int>("revision", row => row.Revision)
    .MapInMemoryProperty<MemoSeed, string>("Source", row => row.Source);

entity.HasProjection<MemoCard>(row => new MemoCard(row.Id, row.Caption,
    EF.Property<int>(row, "revision"), (string)row["Source"]!));
entity.HasQuery("Cards").OrderBy(row => row.Caption).ProjectTo<MemoCard>().List();
```

Enable the existing closed/scripted/store/read options to generate execution. Bindings are absent
by default, independent of presets; no additional service API or CLI option is required.
`MapInMemoryEntity` remains a declaration of ordinary scalar assignments, never an entity factory.
Use named bindings for scalar members that cannot or should not appear in that initializer.

| Model property/read | Fixture lowering |
| --- | --- |
| Exact constant-name, exact-type EF.Property | Bound typed slot, for any declared mapped scalar |
| Actual mapped CLR property or field member | Same slot; a display/behavioral getter is not invoked |
| Constant-key mapped typed indexer | Same slot without calling the indexer |
| Object indexer with an explicit cast to its exact model type | Same slot, including nullable value types and binary results |
| Dynamic/unbound indexer, wrong type, custom method or whole-entity identity | Existing override/diagnostic boundary, not inferred behavior |

Only actual EF metadata members/indexer accessors match. A helper with a similar name is not a
mapped property. Names are trusted design-time metadata, not runtime service property lookups.
Generated members have safe, collision-checked names; no reflection or string-indexer call is
emitted into the fixture implementation. Ordinary C# indexer syntax is retained in production.

## One scalar, one declared source

An initializer assignment and `MapInMemoryProperty` cannot both supply the same EF property,
whether selected through its CLR property or backing field. `RGD4901` rejects this ambiguity rather
than guessing which value to use. Repeating a named declaration replaces that property's recipe.

The value type must equal the model CLR type, including nullable value types/enums; a converter's
provider string/integer type is not a substitute. The existing public scalar subset plus byte
arrays is supported. Recipes must use self-contained portable expressions over the declared public
data type. Captures, clocks, arbitrary helpers, private/model types and invoked delegates are not
evaluated during generation and retain diagnostics. Read-only fields need no fixture setter or
entity materializer; this does not promise that every such model is writable by a real provider.

`WithoutInMemoryProperty(name)` removes one binding. Removing/replacing the store or removing the
entity read map removes dependent bindings. Disabling the read backend removes their output.
These read-only declarations are ignored even when a separate write adapter retains ordinary
scalar helpers; they cannot become write-state overrides.
There is no compatibility alias for the earlier shadow-only `MapInMemoryShadowProperty` API:
rename those calls, and `WithoutInMemoryShadowProperty` calls, to the unified method names.
The [shadow-focused example](../../../samples/ConfigurationExamples/MemoryShadowReads/README.md)
shows the same API without requiring field/indexer concepts.

## Existing query and policy boundaries remain

Named predicates, projections, default/explicit sorting, safe field-name sorting, grouped reports,
aggregates, dictionaries, counted pages, streams and From roots reuse the slots within their existing
portable subsets. This is not a SQL translation guarantee or a client-evaluation fallback.

Each source supplies its own bindings. Separate `UseInMemoryModelQueryFilter` and declared graph
permissions still control final-model filters and relationship reads. Filtered optional references
need local null guards even for a bound property; required filtered references retain overrides.
Related repository visibility is not silently injected. `IgnoreQueryFilters` and designer visibility
bypasses remain independent. Binary result bindings do not widen model-filter operators/types.

Fixture key/copy adapters still own identity, detachment, nested mutable values and baselines.
Reading a named key value does not change the store key or make it a relationship identity map.
Separate per-edge [mapped-key permission](INMEMORY-MAPPED-KEYS.md) can explicitly use bound values
for detached relationship matching. Without it, shadow/private keys and missing ordinary
relationship-key assignments retain the existing boundary.
Copy byte arrays/nested mutable data in the explicit store copy recipe. Snapshots never mutate
arrangement rows; cold streams retain one snapshot per enumeration and scripts retain precedence.

## Inspectable coverage, not tracked state

`RGD4901` covers invalid/mismatched stores/maps, unknown or wrong-type properties, duplicate scalar
sources and nonportable recipes. Strict reads retain `RGD3302` for uncovered access unless explicitly
acknowledged. Non-strict unsupported calls still throw, even on an empty store, until scripted.

Manifests add `readMapping.propertyMappings` with model names/types, slots, recipes and access kinds.
Per-operation `propertyReads` list values consumed by query/filter expressions. Detached adapters
still populate all configured slots; receipts are not a column-pruning or skipped-recipe promise.

This read permission alone does not simulate field/indexer/shadow writes or original concurrency-token
state. Separate [mapped fixture write-state consent](INMEMORY-MAPPED-WRITES.md) permits exact
non-identity scalar state, original-version checks and explicit generated-value factories. Provider
defaults/computed behavior, relationship identity, native returned buffers and saved command results
retain their own guards, opt-ins and typed overrides.
Simple generation, production implementations/contracts, API snapshots and cache shapes are unchanged.

See [MemoryMappedReads](../../../samples/ConfigurationExamples/MemoryMappedReads/README.md) for the
focused designer, four exact outputs and EF/model-free consumer. Regression cases compare actual
generated SQLite/fixture reads, including getter-free members, readonly fields, object/typed/binary
indexers, converted nullable values, reports, pages, safe sorting and filtered ordinary graphs.

[Public read maps](INMEMORY-PROXIES.md) · [Shadow-only walkthrough](INMEMORY-SHADOW-READS.md) ·
[Model filters](INMEMORY-MODEL-FILTERS.md) · [Testing index](README.md)
