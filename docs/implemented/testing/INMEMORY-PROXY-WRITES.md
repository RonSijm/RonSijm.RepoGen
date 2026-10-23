# Explicit public fixture write adapters

`MapInMemoryWrites<TData>((candidate, saved) => ...)` extends optional
[tracked fixture writes](INMEMORY-WRITES.md) to public arrangement rows backed by internal/private
persistence models. It is a separate declaration from [scalar read mapping](INMEMORY-PROXIES.md).
No reverse mapping is inferred from entity or DTO projections, and no persistence entity is constructed.

See [ProxyNotebookRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryProxyWrites/Configuration/ProxyNotebookRepositoryDesigner.cs)
and the [exact designer → output map](../../../samples/ConfigurationExamples/MemoryProxyWrites/README.md).

## Opt in explicitly

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Generated")
    .UseScriptedTestDoubles("Tests.Generated")
    .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
    .RequirePortableTestCommands());

entity.HasInMemoryStore<NoteSeed, int>(data => data.Key,
        data => new NoteSeed(data.Key, data.Caption, data.Revision,
            data.Archived, data.Marker.ToArray(), data.Tag))
    .MapInMemoryEntity<NoteSeed>(data => new PrivateNote
    {
        Id = data.Key, Title = data.Caption, Version = data.Revision,
        Hidden = data.Archived, Stamp = data.Marker
    })
    .MapInMemoryWrites<NoteSeed>((candidate, saved) => new NoteSeed(
        candidate.Id, candidate.Title, candidate.Version, candidate.Hidden,
        candidate.Stamp.ToArray(), saved == null ? "created" : saved.Tag))
    .HasInMemoryGeneratedValue(row => row.Id,
        (row, rows) => checked(rows.Select(item => item.Id).DefaultIfEmpty().Max() + 1))
    .HasInMemoryGeneratedValue(row => row.Version, (row, rows) => row.Title.Length);
```

The public row, persistence entity and result DTO can have different names and shapes. The read map
declares plain scalar slots; the write map receives those slots and a detached saved public row.
`saved` is null for creates and the **current** saved arrangement row for updates. Preserve extra
arrangement-only metadata deliberately, as `Tag` does above. A declared generated-value factory is
still necessary for any required ID/default/computed value; SQL generation is never guessed.

There is no new global switch. All testing options remain off by default. Turning commands off
emits no write adapter or generated-value registration and does not affect production output,
contracts, API snapshots or cache fingerprints. Automatic reads can be independently disabled;
the private scalar helper is then retained only for writes. Scripts retain precedence.

## Required scalar contract

The separate [mapped write-state option](INMEMORY-MAPPED-WRITES.md),
`MapInMemoryWrites(..., useMappedPropertyState: true)`, additionally admits exact named non-identity
scalar bindings and original-version checks. Ordinary primary/FK/unique keys, complete structural
round trips and independent graph/provider restrictions remain. The rules below are the default.

- Declare a matching, distinct public `HasInMemoryStore<TData, TKey>` and `MapInMemoryEntity<TData>`
  **before** `MapInMemoryWrites<TData>`. These must be ordinary mapped scalar values, including
  keys, tokens and columns not assigned by a particular command. A partial read map is insufficient
  for writes: missing state must not acquire invented defaults.
- The storage key directly represents the complete EF primary key in declared order. Each primary
  key assignment must be a same-type public-data member, not arithmetic or an inferred inverse.
  Compound keys require an explicit immutable constructor-shaped key. Bounded batches retain their
  existing single unique-scalar matching rule; compound keys are not new batch support.
- Each Create mapping explicitly assigns every ordinary scalar, except values supplied by explicit
  generated-value adapters. Entity constructors/default initializers/private setter behavior do not
  execute in the fake and cannot supply missing values.
- The write expression uses mapped scalar values and public data, not whole-entity behavior,
  navigation graphs, persistence casts, ambient clocks or captured application services. All adapters
  must be pure, deterministic, non-reentrant and return detached roots/deep-copy mutable values.
- Runtime preparation verifies the copy, storage key and full scalar read → write → read round trip.
  Lossy encoders cannot silently overwrite public rows. A deliberately transformed read scalar must
  have an explicit lossless inverse for all values a supported command can produce.

`WithoutInMemoryWrites()` removes the write adapter and its dependent generated-value factories,
retaining scalar reads, public arrangement storage and scripts. Removing the read map also removes
dependent writes/factories; replacing/removing the store clears all its mappings.

## Shared Stage/Save and public arrangement

Public `Seed`, `SeedRange`, `Find`, `Snapshot`, baseline and inspection helpers remain typed as
`TData`. The generated internal command view shares their backing store identity, rather than
creating a second independent store. Direct runtime writes through the original public-data store
are rejected once that mapped view is registered; arrangement remains available under normal rules.

The production designer supplies predicates, visibility, guards, expected versions, assignments,
permissions, context-key binding and result projections. Commands target generated scalar rows,
not original EF entities. Create/Patch/TrackedDelete and suitable bounded PatchMany/DeleteMany
retain their existing semantics:

- Stage changes pending state only; reads and public inspection see saved public rows.
- Save prepares generated values and public encodings for **all** participating stores before any
  dictionary commits. A mapping failure, conflict or pre-commit cancellation cannot partially save
  another repository. Pending state remains available for explicit resolution; a failing saved command
  restores that command's changes while retaining earlier unrelated staging.
- Only changed scalar rows are encoded. Untouched rows preserve their public metadata; deleted rows
  require no write encoding. Updates receive the latest saved metadata, not stale staging snapshots.
- Saved DTOs contain generated values; delete results describe pre-deletion state. A result-projection
  failure after commit remains `RepositorySavedResultException`, not an unsaved-write outcome.
- One fixture partition owns the shared unit of work. Typed context keys isolate writes/reservations,
  even when keys match. Checkpoint rollback and baseline restore include the public backing stores.

These guarantees depend on honest deep-copy/purity adapters. The runtime validates keys/scalars,
but cannot prove arbitrary getter purity or ownership of arrangement-only nested objects.

## Diagnostics and deliberate limits

RGD3601 rejects invalid write declarations: missing/mismatched maps, incomplete scalar coverage,
transformed/reordered primary aliases or non-portable write expressions. RGD3402 remains the
strict per-command coverage diagnostic; missing Create scalars, unsupported model shapes or missing
required generated-value factories are not silently generated. RGD3501 covers invalid factories.
Non-strict unsupported operations still require a typed script and fail clearly without one.

This is scalar fixture adaptation, **not** inferred graph reconciliation, navigation persistence, EF tracking
or an arbitrary aggregate factory. Shadow/owned/shared/inherited state, active EF filters/AutoInclude,
custom provider converters/comparers, locks/races, database constraints,
triggers and distributed transactions retain their script/real-provider boundaries. Suitable immediate
scalar Update/Delete/returning commands can explicitly use [SimulateImmediateWrites](INMEMORY-IMMEDIATE-WRITES.md)
with these maps, without flushing/refreshing pending writes or implying provider parity. Internal model
types are supported; private scalar CLR types do not leak into the consumer.
Designed leaf-child reconciliation can separately [opt into child-store simulation](INMEMORY-COLLECTIONS.md)
using these explicit maps at both ends, without persisting a navigation graph or inferring reverse mappings.
Suitable scalar InsertIfAbsent/InsertOrUpdate can also [opt into saved-state fixture execution](INMEMORY-ATOMIC-WRITES.md)
with complete public mappings, explicit insert factories and unchanged production capabilities.

The manifest adds a store `writeMapping` receipt with explicit source, saved-data rules, pre-commit
preparation, full scalar/key validation and false flags for persistence construction, inferred reverse
projections, navigation writes and relational equivalence. `readMapping.writes: NotAdapted` still
means the read map alone does not enable writes; this separate receipt is the write declaration.

Tests compile contracts/doubles without a model-assembly reference and compare shared named operations
against actual generated SQLite repositories. These establish the exercised cases, not universal
provider equivalence. See [relational contract fixtures](REPOSITORY-CONTRACT-FIXTURES.md).
