# Explicit nested DTO helper translations

`HasInMemoryProjectionTranslation(..., allowNestedData: true)` enables nested public DTOs and
materialized arrays/lists for one fixture mapper recipe. The default remains flat result data;
no model-wide switch, new runtime dependency or service-side projection API is added.

Start with [BriefingsRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryNestedProjections/Configuration/BriefingsRepositoryDesigner.cs)
and [its exact four outputs](../../../samples/ConfigurationExamples/MemoryNestedProjections/README.md).
The stored row is scalar; headings, facts and words are application result data, not navigations.

## Configure the result once

```csharp
modelBuilder.HasInMemoryProjectionTranslation<int, string, int, BriefingCard>("BriefingCard",
    (id, title, revision) => BriefingCards.Create(id, title, revision),
    (id, title, revision) => new BriefingCard(id,
        new BriefingHeading(title.Trim().ToUpperInvariant(), revision),
        new List<BriefingFact> { new("length", title.Length), new("revision", revision) },
        title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.ToUpperInvariant()).ToArray()),
    allowNestedData: true);

entity.HasProjection<BriefingCard>(row => BriefingCards.Create(row.Id, row.Title, row.Revision));
entity.HasQuery("Cards").OrderBy(row => row.Id).ProjectTo<BriefingCard>().List();
```

Services call CardsAsync. Production keeps the original mapper call. Automatic fixtures expand
the explicitly supplied recipe, including named projection reuse, supported From/group shapes,
pages and cold streams. Command results still need their own UseInMemoryTranslations permission;
enabling a nested read recipe does not enable commands, graph writes or saved-result navigation.

One-to-four inputs are still public ordinary scalars. Every input must remain referenced after
expansion. Matching is still by exact closed method overload, direct inputs and fixed literal/static
symbols; aliases must be acyclic. Child DTO types do not need separate method registrations merely
to construct them: their public data shape is validated recursively against the final EF model.
Registered flat/scalar aliases can compose without gaining permission to construct nested data
inside their own recipes.

## Supported data shapes

The helper's top-level result remains a DTO; collections are result-data members, not a new
collection-root translation API.

- Independent public classes/structs/records with readable public scalar, DTO or collection members.
- Public constructors and explicit writable property/field assignments; conditional null DTOs use
  the normal contract-nullability configuration. Nullable struct DTOs retain their normal types.
- One-dimensional arrays and standard List<T>, with scalar or approved DTO elements. Public members
  may expose IList<T>, ICollection<T>, IReadOnlyList<T>, IReadOnlyCollection<T> or IEnumerable<T>.
- Array literals, Array.Empty<T>(), List<T> constructors/initializers and portable pipelines ending
  in Enumerable.ToArray/ToList. Supported ordering may precede materialization.

An IEnumerable<T> contract does not permit a deferred result. Assign a materialized array/list,
not a naked Select/Where iterator. Standard List<T>(IEnumerable<T>) also materializes its input.
This avoids returning a lazy closure over the fixture's projection row. Implicit nested member or
collection mutation (`Detail = { ... }` / `Items = { ... }`) is rejected: assign fresh data explicitly.

Recursive result types, collection-of-collection shapes, multidimensional arrays, dictionaries,
sets, queryables, object/service/framework/delegate slots and entity-bearing results retain
diagnostics. Entity exclusion applies inside nested properties and generic arguments, including
model-owned/derived entities. Inspection is bounded to sixteen DTO/collection levels and 128 distinct
DTO types, including closed generic shapes; types are inspected without executing their code.

## What stays unchanged?

There is no mapper-body decompilation, reflection invocation, capture evaluation or constructor/
getter execution during generation. Captures, clocks, dropped arguments, private constructors,
static writes, unrelated helpers and custom operators retain the existing translation checks.
DTO constructors/getters execute during result materialization, as with ordinary projections;
their authors still own that behavior. Metadata validation is not a proof of arbitrary DTO purity.

Store/copy/read/write maps, scalar assignments, fixture identities, visibility and provider policies
are not rewritten. Detached results, fresh snapshots, cancellation, scripts, Stage/shared Save,
version conflicts, saved batch ordering and baseline/checkpoint rollback retain their contracts.
This is nested **result data**, not entity-graph mapping or persistence. Related entity inputs and
automatic navigation loading are not enabled by this option.

Production contracts, API/cache expressions and SQL semantics remain unchanged. A helper that EF
can execute in its final client projection can return nested application data. The same opaque
call in a SQL predicate/intermediate stage can still fail translation: no client-filter fallback
is introduced. Actual generated SQLite tests verify the supported final projection/saved-result
cases and deliberately verify this SQL-predicate boundary.

## Diagnostics and receipts

- RGD4601: unsupported result data/model exclusion, recursive or over-limit type shape.
- RGD4602: invalid construction, deferred result collection or nonportable/captured/dropped inputs.
- RGD4001 / RGD4003: unchanged exact-call/name/overload and cyclic-expansion rules.
- RGD3302 / RGD3402: remaining strict read/command coverage without an acknowledged override.

Used recipes record ExplicitNestedDtoAndMaterializedCollectionConstruction beside the existing
exact-match, unchanged-production and no-provider-equivalence receipt. Unused/disabled recipes emit
no helper/receipt. Existing scalar/flat recipes and unconfigured outputs retain their previous bytes.

See [flat recipes](INMEMORY-PROJECTION-TRANSLATIONS.md), [scalar translations](INMEMORY-TRANSLATIONS.md)
and [real-provider contracts](REPOSITORY-CONTRACT-FIXTURES.md).
