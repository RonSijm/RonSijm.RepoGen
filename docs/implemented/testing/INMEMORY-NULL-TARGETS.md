# Explicit null-preserving many-to-many fixture reads

`HasInMemoryNavigation(n => n.Labels, preserveMissingTargets: true)` opts one optional-target
skip navigation into a null-slot fixture policy. A link belonging to an existing source retains
one null element when its target FK is incomplete or does not resolve in the participating snapshot.
Repeated missing-target links retain separate slots, so they still contribute to a raw link count.

This extends [guarded nullable-link reads](INMEMORY-OPTIONAL-LINKS.md). It does not enable SQL's
general null propagation or change production expressions. Start with
[NullTargetNotebookRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryNullTargets/Configuration/NullTargetNotebookRepositoryDesigner.cs)
and its [exact designer → output map](../../../samples/ConfigurationExamples/MemoryNullTargets/README.md).

## One opt-in, shared designer expressions

```csharp
entity.HasInMemoryNavigation(row => row.Labels, preserveMissingTargets: true);
entity.HasProjection<NotebookSummary>(row => new NotebookSummary(
    row.Id, row.Title,
    row.Labels.Count,
    row.Labels.Count(label => label == null),
    row.Labels.OrderBy(label => label == null ? 0 : label.Id)
        .Select(label => label == null ? null : label.Name).ToList()));
entity.HasQuery("Summaries").ProjectTo<NotebookSummary>().List();
```

Consumers only call `SummariesAsync()`; null handling stays in the designer. The projection can be
reused in pages, streams, typed From sources and fresh single/batch saved results.

The option defaults to false. Calling the original one-argument declaration or the overload with
false resets it; WithoutInMemoryNavigation removes it. It adds no global flag, production dependency,
query rewrite, registration or consumer-side query API. Automatic fixture stores/reads and selected
endpoint/join stores remain independent prerequisites. Disabled fixture reads ignore the option.

Only an explicitly mapped many-to-many navigation with an optional **target** FK accepts this policy.
The inverse edge is configured independently; optional sources still belong to no missing owner.
Ordinary references/collections or required targets fail declaration validation with RGD4401.

## Bounded null-safety proof

The generator checks every navigation occurrence, including reused expression DAG nodes. It never
executes designer getters/helpers/comparisons to establish safety. Built-in reference null tests,
short-circuit guards and conditional branches establish non-null state locally, never globally.

| Shared expression | Automatic coverage |
| --- | --- |
| Labels.Count / Count() / LongCount() | Includes retained null-target slots |
| Any(label => label == null) / Count(label => label == null) | Explicit missing-target checks |
| Any(label => label != null && label.Name == value) | Safe guarded membership |
| Where(label => label != null).Select(label => label.Name) | Existing non-null semantics |
| OrderBy(label => label == null ? 0 : label.Id) | Explicit null-safe sort key |
| Select(label => label == null ? null : label.Name) | Deliberate nullable scalar result |
| Labels.Select(label => label.Name) | RequiresOverride: unsafe CLR dereference |
| Labels.OrderBy(label => label.Id).Where(label => label != null) | RequiresOverride: guard is too late |
| Raw entity collection, entity/object escape, entity Distinct or unrecognized operators | RequiresOverride |
| Explicit Include/ThenInclude/load profile | RequiresOverride: different materialization semantics |

The bounded entity-sequence pipeline includes Where, Select, built-in OrderBy/ThenBy variants,
Skip/Take and scalar Count/LongCount/Any/All. Supported scalar/DTO projections can use list/array
materialization and existing scalar terminal operations. Indexed selectors/predicates, custom
comparers and overloaded entity equality are not safety proofs. Existing portable-method,
mapping, nullability and identity boundaries still apply; this option cannot bypass them.

Unsafe queries remain RequiresOverride, including on empty stores; strict reads reject them with
RGD3302 and strict saved-result commands with RGD3402. Typed RequireTestOverride/scripts continue
to take precedence over automatic execution. Null-safe DTO members should declare their actual
nullable element types; this policy does not rewrite application contracts.

## Coordinated snapshots and relational limits

Only declared/accessed edges are rebuilt from the same detached source/target/join snapshot.
Link order is preserved unless the shared expression sorts it. Returned DTO/list mutation cannot
affect stores, baselines, fresh reads or an already captured stream enumeration. Pages retain root
visibility and the same nullable projection. Related visibility is not implicitly injected.

Principal keys and complete source/target pairs must remain unique. Repeated incomplete target
keys do not become duplicate complete pairs. Key arity/shape, required references and cancellation
are still checked before relationship assignment. Missing optional sources never manufacture owners.
An unresolved non-null optional target can be represented in a fixture, but a real database FK
constraint may reject that row: fixtures are not database-constraint enforcement.

SQL may retain null targets in scalar/DTO collection projections while entity Include materialization
omits absent related entities. Therefore the flag does not authorize explicit loading or whole-entity
results. Nor does it translate an unsafe `label.Name` access into SQL-style null propagation.
Actual generated SQLite contract cases cover counts, presence checks, null-safe DTOs, both directions,
pages and saved results; they do not establish every provider/expression/concurrency behavior.

Opted-in join receipts report PreserveMissingTargetPerJoinRow, ExplicitNullSafeClrExpressionsOnly
and the fixture-only loading boundary. Unconfigured/required/guarded-only receipts remain unchanged.
Implicit/shared/owned/inherited joins, shadow/custom-converted keys, model filters/AutoInclude,
filtered loading and graph mutation retain their prior adapter/provider boundaries.
