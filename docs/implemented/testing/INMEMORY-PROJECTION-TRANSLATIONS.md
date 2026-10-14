# Explicit flat DTO helper translations

HasInMemoryProjectionTranslation extends optional fixtures for scalar-to-DTO mapping helpers.
Production retains the original call; the designer explicitly declares a fixture expression.
This is not method-body inspection, entity graph mapping or automatic SQL translation.

Start with [ProjectionMemosRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryProjectionHelpers/Configuration/ProjectionMemosRepositoryDesigner.cs)
and its [exact designer → output map](../../../samples/ConfigurationExamples/MemoryProjectionHelpers/README.md).

## Configure once, expose named operations

```csharp
modelBuilder.HasInMemoryProjectionTranslation<int, string, int, MemoCard>(
    "MemoCard",
    (id, text, revision) => MemoCards.Create(id, text, revision),
    (id, text, revision) => new MemoCard(
        id, text.Trim().ToUpperInvariant(), revision, text.Length));

entity.HasProjection<MemoCard>(row => MemoCards.Create(row.Id, row.Text, row.Revision));
entity.HasQuery("Cards").OrderBy(row => row.Id).ProjectTo<MemoCard>().List();
```

Services call CardsAsync, never a fixture projection chooser. Result formatting does not rewrite
stored scalar assignments. Commands need their independent UseInMemoryTranslations opt-in,
including Returns/ReturnsSaved. Backend/store/map, visibility, permission/version and save
configuration remain separate. Nothing is registered by default; unused registrations emit no
receipt/helper and disabled backends retain scripts. Existing scalar HasInMemoryTranslation
registrations do not become DTO/graph escape hatches.

## Bounded flat result data

One-to-four inputs must be public ordinary scalars, with matching call/replacement result CLR
types. DTOs are independent public application classes/structs with readable public scalar
properties/fields. Records and public scalar initializers work; conditional null results use
the normal explicit contract-nullability annotations. Nested DTOs/collections, entity or
entity-derived/generic entity-bearing results, service/framework types, private/abstract/interface
types, indexers and arbitrary value-object graphs are excluded using the final EF model.

Replacements explicitly construct the result through public scalar constructors or writable
scalar member initializers. They use the portable framework subset and can compose registered
scalar translations and acyclic projection aliases. Every input remains referenced, so replacing
a call cannot erase an unmapped scalar/navigation argument from coverage checks.

Generation inspects expressions/types/member metadata without invoking source methods, constructors,
getters or either lambda. Constructors/getters execute during result materialization, just like
ordinary DTO projections; authors own their behavior. This is not a proof of arbitrary DTO purity.

Matching shares [scalar translation rules](INMEMORY-TRANSLATIONS.md): public closed overloads,
direct inputs, fixed literal/static symbols, no ref/out/by-ref returns, and shared name/overload
uniqueness across modules. Closed generic calls and fixed literals are not wildcards. Captures,
ambient clocks, unrelated helpers, delegates/custom operators, private constructors and graph
construction fail. Unmatched operations require overrides, including on empty stores.

## Production and fixture semantics remain distinct

Named projections, supported From/group shapes and independently opted-in saved scalar results
use fixture expansion. Pages, cold streams, detached results, staged/shared saves, input-ordered
saved batches, cancellation and script precedence retain their contracts. Store/copy/read-map/
write-map/value adapters are not rewritten; graph/provider/loading boundaries are not relaxed.

Production APIs, expressions, dependencies/cache fingerprints and contracts are unchanged. EF
may execute a helper in its final DTO projection after selecting scalar arguments. The same opaque
helper in a SQL filter, ordering or intermediate stage may still fail translation. A fixture
recipe does not make that helper SQL-translatable or introduce a client filter fallback.

Shared generated SQLite cases verify top-level DTO helpers, initializers, struct/null results,
groups/pages and fresh single/batch DTOs. A separate case deliberately proves that a helper
predicate fails in production while its explicit fixture recipe executes. These tests supplement,
not replace, [real-provider fixtures](REPOSITORY-CONTRACT-FIXTURES.md).

## Diagnostics and receipts

- RGD4601: unsupported flat DTO result or absent model-aware entity validation.
- RGD4602: nonportable/captured/dropped-input/invalid DTO construction replacement.
- RGD4001: shared name/overload/call/direct-input/fixed-slot violations.
- RGD4003: recursive/cyclic expansion.
- RGD3302 / RGD3402: remaining unacknowledged read/command coverage.

Used registrations add ExplicitFlatDtoConstruction to their expressionTranslations entry,
alongside exact-match, unchanged-production and no-provider-equivalence receipts. Existing
scalar receipts and unconfigured generated outputs remain identical.
