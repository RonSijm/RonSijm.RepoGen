# Optional final-model filters in fixture reads

`entity.UseInMemoryModelQueryFilter()` lets generated in-memory read operations reuse a portable
row-only filter from the final EF model. It is **off by default**, independent of
`UseInMemoryTestQueries()` and unrelated to production filter configuration. Simple users receive
no extra generated infrastructure. It neither discovers tenant context values nor duplicates
the filter in a second designer expression.

Start with [FilteredNotebookRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryModelFilters/Configuration/FilteredNotebookRepositoryDesigner.cs)
and its [exact designer → output map](../../../samples/ConfigurationExamples/MemoryModelFilters/README.md).
The two small [context tables](../../../samples/ConfigurationExamples/MemoryModelFilters/Model/FilteredNotebookContext.cs)
declare soft-delete/archive filters; the designer opts each root into fixture execution.

## Configure once, select fixture behavior explicitly

```csharp
// EF model, not the repository designer:
modelBuilder.Entity<Note>().HasQueryFilter(note => !note.Deleted);

// RepositoryDesigner, with stores/read backend configured:
modelBuilder.Entity<Note>(entity =>
{
    entity.UseInMemoryModelQueryFilter();
    entity.HasVisibilityPolicy("Visible", note => !note.Hidden);
    entity.HasQuery("Cards").OrderBy(note => note.Id)
        .Select(note => new NoteCard(note.Id, note.Text)).List();
    entity.HasQuery("IncludingDeleted").IgnoreQueryFilters()
        .Select(note => new NoteCard(note.Id, note.Text)).List();
});
```

Use `UseInMemoryModelQueryFilter(false)` to remove the opt-in. A missing model filter or disabled
read backend adds no filter source/receipt. Production repositories, ports, cache/API shapes and
the actual EF model remain unchanged.

## Where the filter applies

- Ordinary entity-root reads filter detached rows before designer visibility, predicates,
  sorting, projections, grouping, dictionary/scalar terminals and paging.
- Every participating `From(sources => sources.Set<T>()...)` root opts in independently. Each
  root is filtered before joins, correlated subqueries, self-joins and set operations. Participating
  stores still share one snapshot and fixture partition; repeated roots do not multiply filters.
- Counted pages count the filtered sequence. Cold streams capture/filter a detached snapshot
  at enumeration; later fixture changes affect fresh reads, not an existing enumeration.
- Filtering does not remove saved fixture rows, mutate copies, or change seed/baseline behavior.
- `IgnoreQueryFilters()` bypasses all EF model filters for that operation, including `From` roots.
  `BypassVisibility("Visible")` bypasses only that designer policy. Neither implies the other.

This read-only slice does not enable filtered command simulation, child reconciliation or
`ReturnsSaved` on filtered roots. [Per-command model filters](INMEMORY-MODEL-WRITE-FILTERS.md)
now separately opt eligible scalar writes/fresh results into those semantics; the read setting
never enables them implicitly. [Ordinary filtered navigation reads](INMEMORY-FILTERED-NAVIGATION.md)
now have a separate `includeNavigations: true` permission on every filtered participant;
filtered command graphs still retain overrides.

## Published expression boundary

Supported filters have one ordinary row parameter, a Boolean result and public ordinary mapped
scalar properties. Portable literals, scalar operators and the existing pure scalar expression
subset are emitted as typed C# lambdas. Public mapped fixture rows must map every accessed scalar;
internal persistence entities are not constructed in the consumer.

[Explicit shadow read bindings](INMEMORY-SHADOW-READS.md) additionally supply exact model-only scalar
values from public fixture data. They do not enable this filter permission or tracked shadow state.

[Exact scalar translations](INMEMORY-TRANSLATIONS.md) may replace explicitly registered helper/provider
calls. The replacement still passes row/scalar/member/capture checks. Expressions, constructors,
context properties and source helper bodies are not executed during generation.

The following continue to require `RequireTestOverride(reason)`/a typed script or a real provider:

- Unbound captured contexts, variables, collections/services, ambient static properties and clocks.
  [Explicit context-member bindings](INMEMORY-MODEL-FILTER-CONTEXTS.md) can replace exact scalar
  context symbols with typed partition-key values without evaluating a captured context.
- Whole-entity identity/behavior, navigation predicates, unmapped properties, unbound shadow/field-only
  properties, inheritance/discriminators and active AutoInclude semantics. A named-query
  [IgnoreAutoIncludes policy](../queries/EXPLICIT-LOADING-POLICIES.md) independently suppresses user-configured
  model loading, without bypassing these filters or owned loading.
- Active filtered navigation graphs without the separate graph opt-in, required references to
  filtered targets, implicit/shared many-to-many joins, and unsafe optional-reference access.
  EF required relationships can prune roots; merely filtering related arrays is not equivalent.
  Global `IgnoreQueryFilters()` retains the existing declared unfiltered-navigation subset.
  [Explicit CLR many-to-many filters](INMEMORY-FILTERED-LINKS.md) use the separate graph permission
  on every filtered endpoint/join dataset; optional-target rules remain independent.
- Arbitrary entity graph construction or an unregistered helper/provider method.

Generation reports these through existing operation coverage reasons. Strict
`RequirePortableTestQueries()` raises `RGD3302` unless acknowledged. Unsupported unarranged calls
fail rather than return an empty result; normal typed scripts still override generated behavior.
Successful operation receipts identify roots/predicates, opt-in, application order and bypass scope.

Execution remains LINQ-to-Objects. Null safety, string/collation behavior and other CLR/provider
differences remain explicit author responsibilities, not a general SQL filter emulator.
SQLite contract cases exercise real generated roots, multi-root reads, bypasses, pages and streams.
An opaque method in a production EF model filter still fails SQL translation: the fixture recipe
does not add a client-filter fallback or prove provider equivalence.
