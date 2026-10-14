# Nullable many-to-many fixture links

The optional fixture-read backend can reuse designer queries over explicit CLR join stores whose
foreign keys are nullable. It distinguishes the **source** and **target** of each accessed edge,
rather than assuming that any optional FK makes the whole relationship unsafe.

Start with [LinkedNotesRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryOptionalLinks/Configuration/LinkedNotesRepositoryDesigner.cs)
and its [exact designer/output map](../../../samples/ConfigurationExamples/MemoryOptionalLinks/README.md).
This extends [explicit many-to-many reads](INMEMORY-MANY-TO-MANY.md), not graph writes or SQL simulation.
An independently configured [null-preserving policy](INMEMORY-NULL-TARGETS.md) now supports raw counts,
presence checks and explicit null-safe scalar/DTO expressions. The default guarded-only path below
remains unchanged; general SQL propagation and explicit optional-target loading are not inferred.

## Designer semantics, not a second query API

```csharp
entity.HasInMemoryNavigation(note => note.Labels);
entity.HasQuery("Cards").OrderBy(note => note.Id)
    .Select(note => new NoteLabels(note.Title,
        note.Labels.Where(label => label != null)
            .OrderBy(label => label.Id).Select(label => label.Name).ToList()))
    .List();
```

The EF model defines `LabelId` as optional on an explicit `NoteLabelLink`. The designer supplies
selected endpoint/link stores and any public scalar mappings, just as for required links.
The generated consumer only calls `CardsAsync()`. It does not supply EF expressions or null filters.

`UseInMemoryTestStores`, `UseInMemoryTestQueries` and each `HasInMemoryNavigation` remain opt-in.
There is no additional generation switch, no production registration change and no added API
when fixture generation is disabled. Strict coverage is configured through `RequirePortableTestQueries`.

## Why the direction matters

For `Note.Labels`, the link's Note FK is the source FK; Label FK is the target FK. For
`Label.Notes`, those roles reverse. A nullable source key cannot correlate to an existing source
row. A nullable target key, however, can retain a null target in a relational collection projection.

| Edge shape | Automatic fixture coverage |
| --- | --- |
| Required source, required target | Existing declared many-to-many subset |
| Optional source, required target | Supported; null/unmatched source keys belong to no source row |
| Required or optional source, optional target | Supported only when every use starts with `Where(target => target != null)` |
| Optional target, unguarded count/projection/membership or direct collection result | RequiresOverride by default; explicit null-preservation enables only the reviewed null-safe scalar/DTO subset |
| Optional target in an explicit Include/ThenInclude/load profile | RequiresOverride |

The explicit filter deliberately removes missing targets from the query. The loader can then build
only non-null related rows without inventing SQL null propagation. The same rules apply to
predicates, named projections, replacement `From` expressions, pages, cold streams and
[single/batch saved command results](../writes/SAVED-COMMAND-RESULTS.md).

SQLite regression tests demonstrate why this check is necessary: one valid owner with two targets
and two null-target links produces an unguarded count of four, but a guarded count of two.
An unguarded scalar-name projection can contain null elements. Silently treating all missing
targets as an empty relationship would change these results. This observation concerns the tested
EF/SQLite queries, not a promise of identical behavior across providers or arbitrary projections.

## Conservative guard recognition

The filter must be the first operation on that navigation sequence. Accepted forms are a direct
built-in reference comparison, `target != null` or `null != target`, and a leading short-circuit
guard such as `target != null && !target.Hidden`. Later ordering/projection/aggregation/membership
uses that explicitly non-null sequence.

These shapes require an override:

```csharp
note.Labels.Count                              // observes null-target link rows in SQL
note.Labels.Any(label => label != null)         // no explicit filtered sequence
note.Labels.OrderBy(label => label.Id)
    .Where(label => label != null).Count()     // guard is too late for this bounded lowering
note.Labels.Where(label => label != null || label.Hidden).Count()
```

One guarded use does not authorize an unguarded use elsewhere in the same operation. The check
is occurrence-based, including expression DAGs that reuse the same member node. Custom overloaded
equality operators, opaque helpers and equivalent but unrecognized logical rewrites are not proof
of a non-null sequence. Ordinary portable-expression/member validation still applies.

Inferred projection paths do not add an unguarded observation: the actual shared projection
supplies the guard. Explicit loading selectors do observe the raw navigation and retain the
override boundary. No production Include, predicate, result type or cache fingerprint is rewritten.

## Detached snapshot and validity

Both endpoint stores and the link store are captured together under the fixture-partition lock.
Copying precedes loading; each list retains link-snapshot order unless the designer sorts it.
Returned DTO/list mutation cannot affect stores, baselines, later reads or an already captured stream.

Null in any composite FK component never matches. Null/incomplete pairs do not count as duplicate
complete relationship pairs; repeated complete pairs still fail. Principal relationship keys remain
unique, and the other required FK still must resolve even when the optional FK is null. Key shape
validation and cancellation are never disabled by optionality. Optional unmatched non-null keys
are absent from the filtered fixture relationship; this does not validate database FK constraints.

Only declared/accessed edges load. Root visibility stays on its root; related visibility is an
explicit shared filter, not an automatically injected policy. Both entity-shaped and fully mapped
public stores are supported. Internal/read-only persistence navigations need not be constructed
or assigned when public read rows are used.

Optional-edge manifests add `optionalSource`, `optionalTarget` and `nullPropagation` to the existing
join receipt. Required-edge receipts and existing generated outputs remain unchanged.
Unsupported reads fail with `UnsupportedInMemoryRepositoryOperationException`, including on empty
stores; strict generation rejects unacknowledged queries with RGD3302 and commands with RGD3402.
Use `RequireTestOverride(reason)` and typed scripts, or a
[real-provider fixture](REPOSITORY-CONTRACT-FIXTURES.md), for deliberately provider-dependent behavior.

Implicit/shared property-bag joins, shadow/custom-converted keys, EF model filters/AutoInclude,
filtered loading and broader graph writes retain their existing boundaries. This feature does
not mutate many-to-many membership or infer aggregate persistence rules.
