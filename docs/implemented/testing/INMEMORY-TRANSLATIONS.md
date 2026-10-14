# Designer-declared fixture expression translations

`HasInMemoryTranslation` extends the optional F15.3 read backend for calls that otherwise need
a typed script: provider functions and application-owned scalar helpers. Configure their fixture
semantics once, alongside the production queries. Services still call named repository methods.

Start with [PatternCatalogRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryTranslations/Configuration/PatternCatalogRepositoryDesigner.cs)
and the example's [exact designer → output map](../../../samples/ConfigurationExamples/MemoryTranslations/README.md).

## Separate production from test behavior

```csharp
modelBuilder.HasInMemoryTranslation<string?, string?, bool>(
    "OrdinalIgnoreCasePatterns",
    (value, pattern) => EF.Functions.Like(value, pattern),
    (value, pattern) => InMemoryStringPatterns.Like(
        value, pattern, StringComparison.OrdinalIgnoreCase));

modelBuilder.HasInMemoryTranslation<string, int>(
    "FixtureLength",
    value => PatternFunctions.Length(value),
    value => value.Length);

entity.HasQuery("Search")
    .Where<PatternSearch>((row, request) =>
        EF.Functions.Like(row.Title, request.Pattern)
        && PatternFunctions.Length(row.Title) >= request.MinimumLength)
    .OrderBy(row => row.Id)
    .ProjectTo<PatternCard>().List();
```

Production retains the original EF/provider/helper calls, SQL mapping, dependencies, cache
fingerprints and contract signatures. Only the independently generated fixture expression view
uses the replacement. Neither lambda is compiled or invoked during generation; captured values
and property getters are not evaluated to discover semantics.

No translations are registered by default. Automatic reads still require closed test output,
selected stores/maps and `UseInMemoryTestQueries()`. Registering an unused translation leaves output
unchanged; disabling automatic reads retains scripts and does not emit translation receipts.
Declarations can be registered in model configuration modules as well as the main designer.
Commands reuse registrations only with a separate per-command
[UseInMemoryTranslations opt-in](INMEMORY-COMMAND-TRANSLATIONS.md).
Independent [flat DTO helper translations](INMEMORY-PROJECTION-TRANSLATIONS.md) use
HasInMemoryProjectionTranslation with scalar inputs and explicit public DTO construction. Ordinary
scalar declarations retain their existing boundary; nested/entity graphs are not implicitly allowed.

## Exact, typed matching

The typed overloads accept one to four inputs and the same scalar result type on both sides.
Inputs/results must be public primitive/enum/string/decimal/Guid/date/time scalars, optionally
nullable; entire entities, services, query roots and arbitrary value-object graphs are excluded.

The source must be a single public closed method call, without ref/out/in inputs or ref returns. Each input occurs exactly once as a direct
argument or instance receiver. Other slots are fixed literals or static member symbols, such as
`EF.Functions`. Matching compares actual method metadata, closed generic arguments and those fixed
slots; it does not search/replace C# text, use field-name reflection or execute the source receiver.

For example, a separate overload can deliberately require one fixed escape character:

```csharp
modelBuilder.HasInMemoryTranslation<string?, string?, bool>(
    "EscapedPatterns",
    (value, pattern) => EF.Functions.Like(value, pattern, "!"),
    (value, pattern) => InMemoryStringPatterns.Like(
        value, pattern, StringComparison.OrdinalIgnoreCase, '!'));
```

That declaration does not match another escape string or the two-argument Like overload. A
closed `Echo<int>` translation does not match `Echo<string>`. A method overload has only one
registration, even if different fixed-literal patterns could be imagined; names and overloads
must remain unique across modules. This deliberately avoids overlapping precedence rules.

Replacement bodies use the conservative portable framework expression subset, plus the explicit
EF-free pattern helper. They may call another registered translation; nested arguments expand
before the outer call, and recursive/cyclic replacement graphs fail. Every input remains referenced,
so removing a parameter cannot conceal an unmapped scalar/navigation argument. Captured objects,
clocks/static state, invoked delegates, arbitrary helper calls and graph construction are rejected.
Scalar request values should be immutable/pure: replacement expressions may reference them more
than once, and declaration inspection is not a general-purpose purity or SQL-equivalence proof.

## Read coverage and boundaries

Translations apply before portable inspection and rendering to active root predicates,
visibility/default/explicit sorts, reusable criteria, named/custom projections, grouping,
result-stage filters/maps/ranking/limits/field sorts, membership keys, scalar/dictionary terminals,
cursor seek/selection/guards, request guards, context keys and supported typed From/Set composition.
Entity-shaped and mapped public stores share the mechanism. Streams retain ordinary cold snapshot,
cancellation, recording and disposal behavior; scripts continue to take precedence.

All existing coverage checks remain: mapped-member completeness, declared relationships, model
filters/AutoInclude/inheritance, raw/temporal roots and relational isolation are not bypassed by
registering a scalar call. Unmatched/nonportable operations require explicit overrides, including
empty-store calls. `RequirePortableTestQueries()` still enforces that boundary.

Named fixture reads use applicable registrations automatically when their backend is enabled.
The separate [command opt-in](INMEMORY-COMMAND-TRANSLATIONS.md) covers supported tracked/immediate
command selection/setters, collections/links and Returns/ReturnsSaved. Store key/copy/read-map/
write-map/generated-value adapters are not rewritten. All backends retain their structural/coverage
rules; native constraints, locking, generated rowversions and transaction behavior are not simulated.

## Explicit pattern semantics

`RonSijm.RepoGen.Testing.InMemoryStringPatterns.Like` is an EF-free utility with:

- `%`: zero or more UTF-16 code units; `_`: exactly one code unit.
- Literal brackets, including `[abc]`; no bracket ranges or regex syntax.
- Explicit `Ordinal` (default) or `OrdinalIgnoreCase`, applied to literal code units; other
  comparison modes are rejected rather than depending on ambient culture.
- Optional `char?` escape quoting the next code unit; an unmatched trailing escape is invalid.
- Null input/pattern never matches. Empty patterns match only empty values.
- Dynamic programming rather than regex/backtracking: time is proportional to value length ×
  pattern length, with two value-length buffers. It is not an unbounded-input performance guarantee.

Choose and verify the policy deliberately. Database collation, Unicode/code-point lengths and
case folding, provider bracket/escape rules and SQL null behavior can differ. The shared SQLite
cases prove selected ASCII patterns/native length queries, not SQL Server or universal parity.
Use the [real-provider harness](REPOSITORY-CONTRACT-FIXTURES.md) for actual provider guarantees.

## Receipts and diagnostics

Operations add `expressionTranslations` only when declarations actually participate. Receipts list
their stable names/methods, exact matching, fixture-only semantics, unchanged production expressions
and no expression execution during generation. Coverage says **designer-declared test semantics**,
not automatically inferred provider support. Scripted overrides do not change static coverage.

- RGD4001: duplicate name/overload or invalid call/input/result/fixed-slot shape.
- RGD4002: a captured/nonportable/graph replacement or a dropped input.
- RGD4003: recursive replacement expansion.
- Existing RGD3302: unacknowledged operation remains outside the supported read backend.
- Existing RGD3402: unacknowledged operation remains outside the supported command backend.
