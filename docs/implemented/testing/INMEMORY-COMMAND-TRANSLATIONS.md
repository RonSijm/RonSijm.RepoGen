# Per-command fixture expression translations

Registered [scalar-call translations](INMEMORY-TRANSLATIONS.md) can also be used by generated
fixture commands. Each command chooses `.UseInMemoryTranslations()` explicitly; the default is
**off**, even when reads use those same registrations. Production expressions never change.

Start with [PatternNotesRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryWriteTranslations/Configuration/PatternNotesRepositoryDesigner.cs)
and its [designer → actual output map](../../../samples/ConfigurationExamples/MemoryWriteTranslations/README.md).

## Three separate choices

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Repositories")
    .UseScriptedTestDoubles("Application.Testing")
    .UseInMemoryTestStores().UseInMemoryTestCommands()
    .RequirePortableTestCommands());

modelBuilder.HasInMemoryTranslation<string, string, bool>(
    "OrdinalPatterns",
    (value, pattern) => EF.Functions.Like(value, pattern),
    (value, pattern) => InMemoryStringPatterns.Like(
        value, pattern, StringComparison.OrdinalIgnoreCase));

entity.HasCommand("Rename")
    .Where<RenameRequest>((row, request) =>
        row.Id == request.Id && EF.Functions.Like(row.Title, request.Pattern))
    .Patch(setters => setters.SetProperty<string, RenameRequest>(
        row => row.Title, (row, request) => request.Title))
    .Save().UseInMemoryTranslations();
```

The automatic command backend, model registrations and per-command opt-in are independent.
Declare the entity store/copy policy or complete public read/write maps as usual. Calling
`.UseInMemoryTranslations(false)` turns the command back off. Commands with only portable calls
need no registration or opt-in; registering an unused translation emits no new receipts/output.
Disabling automatic commands keeps scripts and never rewrites command expressions.

`UseInMemoryTestQueries()` is not needed for scalar command results. It is still required for
navigation-dependent `ReturnsSaved`, together with explicit stores/relationship declarations.
Named reads automatically reuse applicable registrations when their own backend is enabled.

## Covered command expressions

| Declaration | Fixture-only expansion |
| --- | --- |
| Where, active root visibility, Validate, UseContextKey | Selection, request guards and explicitly bound scalar partition keys |
| Create / CreateMany | Scalar assignments; bounded item input composition and each creation mapping |
| Patch / tracked Delete | Setter values/conditions, expected-version values and scalar Returns |
| PatchMany / DeleteMany | Item inputs/matching-key values, setters/conditions, expected versions and input-ordered results |
| ReconcileCollection / ReconcileLinks | Item input/key values, conditions, child/link visibility, scalar create/update payloads and expected versions |
| ReturnsSaved | Scalar/DTO projection and active root visibility, including supported declared navigation-read expressions |
| Opted-in immediate Update/Delete/insert/upsert | Candidate scalar assignments, selection/setters/tokens and returned values |

Immediate commands also need `SimulateImmediateWrites()`; collections/links need
`SimulateCollectionWrites()`. A translation does not enable either backend or expand its structural
capabilities. Bypassed visibility remains bypassed; unrelated policies are not injected into a
saved graph projection. Explicit nullable input handling and collection limits remain designer-owned.

The source/replacement still use exact closed method metadata, fixed literals/static symbols,
one-to-four public scalar inputs/results and a conservative portable replacement expression.
Every source input occurs exactly once as a direct argument/receiver; every replacement input
remains referenced. Nested registered calls expand; captures, clocks, arbitrary graph/delegate
construction, ambiguity and recursion fail with RGD4001–RGD4003. Generation does not compile or
invoke either expression, or evaluate captured getters.

## What cannot change

Production source, public signatures, cache fingerprints, provider mappings and dependency
analysis retain the original expressions. Only a separate fixture rendering view uses expanded
values. Store key/copy declarations, MapInMemoryEntity, MapInMemoryWrites, generated-value factories
and comparer declarations are **not** translated; these have their own explicit adapter contracts.

All production structural checks and fixture coverage remain in force: protected scalars need
AllowWrite; immutable keys/matching components/owner FKs remain immutable; complete scalar maps,
ordinary supported relationships, bounded inputs, generated values, conversions/comparers and
model filters/AutoInclude retain their existing requirements. Navigation-dependent write
selection/setters remain unsupported even if every individual scalar helper is translated.

Stage still changes only pending state; Save still commits the **shared** unit of work. Immediate
writes still operate on saved rows without flushing/refreshing pending writes. Version conflicts,
atomic batch preparation, checkpoint rollback and committed-result failure behavior remain unchanged.
SQL constraints, FK existence, cascades, triggers, collation, locks, races and distributed transactions
are not supplied by translations. Real-provider tests remain necessary.

## A production helper still needs a production implementation

A SQL-only marker may be used in an EF-translated predicate or fresh saved projection. It cannot
be put in a production tracked setter/guard and expected to work just because its fixture is
translated: those production expressions run in CLR code. The example deliberately separates a
real CLR `Normalize` helper from SQL-only `Length`, whose SQLite mapping is declared in its context.

Choose replacement semantics deliberately. The shared fake/generated-SQLite cases verify selected
ASCII wildcard/length behavior, tracked CLR setters, versions, Stage/Save and rollback; they do not
prove all providers or Unicode/collation equivalence. See the [real-provider harness](REPOSITORY-CONTRACT-FIXTURES.md).

## Coverage and overrides

Used registrations appear in each command's `expressionTranslations` receipt with exact-match
identity and fixture-only, non-provider-equivalent semantics. Static coverage remains explicit.
Unmatched overloads/literals or remaining unsupported behavior require a typed script; strict
`RequirePortableTestCommands()` rejects unacknowledged cases with RGD3402. Failure does not depend
on finding a row: unsupported operations fail on an empty store too.

`RequireTestOverride(reason)` always retains the override requirement, even when all calls have
translations. Arranged scripts still take precedence over automatic execution, and normal
cancellation/recording/reset behavior is unchanged.
