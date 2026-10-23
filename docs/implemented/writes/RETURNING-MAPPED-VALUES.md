# Mapped scalar values in native write results

`IncludeMappedProperties()` explicitly permits a native `ReturnsMany` command to capture field-only,
field-backed and indexer-mapped scalar values, alongside ordinary CLR and shadow properties.
It is a per-command generation setting, **off by default**. Existing simple/ordinary output and
the narrower [IncludeShadowProperties](RETURNING-SHADOW-VALUES.md) behavior are unchanged.

Services still call named operations returning application-owned request/DTO types. Mapped names,
entity types, native scalar buffers and writer interfaces stay inside the designer/infrastructure.

## Configuration

```csharp
entity.HasCommand("Rename")
    .Where<CapturedNoteRequest>((row, request) => row.Id == request.Id)
    .Update(setters => setters
        .SetProperty<string, CapturedNoteRequest>(row => row.Title, (row, request) => request.Title)
        .SetProperty(row => row.ChangeCounter, row => row.ChangeCounter + 1))
    .ExpectVersion<int, CapturedNoteRequest>("version", request => request.ExpectedVersion)
    .ReturnsMany(row => new CapturedNoteChange(
        row.Id, row.Title, row.NormalizedTitle,
        EF.Property<int>(row, "version"), EF.Property<string?>(row, "SourceTag")))
    .IncludeMappedProperties();
```

Choose a native writer with `UseReturningWrites<TWriter>()` in closed mode first. Bundled
`SqliteReturningWriter` and `SqlServerReturningWriter` support mapped capture for immediate native
`Update`/`Delete` plus `ReturnsMany`. Separately configured [atomic insert/upsert capture](ATOMIC-SCALAR-CAPTURE.md)
uses UseAtomicWrites and a scalar Returns projection, with its own writer capabilities and transaction
policy. Tracked Stage/Save, count-only writes and ordinary query projections do not acquire capture.

`IncludeMappedProperties(false)` removes only this broad permission. A separately configured
`IncludeShadowProperties()` remains effective, but still does not permit field/indexer mappings.
Enabling a writer or declaring a named expected version never enables capture implicitly.

The [MappedReturning example](../../../samples/ConfigurationExamples/MappedReturning/README.md)
maps its designer to four exact generated artifacts and an executable SQLite consumer.

## Supported mapped state

- Every mapped scalar must have a distinct column in the same ordinary table. Non-shadow values
  need an EF-configured materialization member: a writable instance field, a supported property,
  or a writable single-string-key indexer. Readonly, literal and static fields are rejected.
- Getter-only CLR properties can use an explicitly mapped writable backing field. EF property
  access mode determines materialization; the generator and runtime share the same metadata
  validation without running entity constructors/getters during generation.
- Field-only model property names must match their actual fields as required by EF. Native
  return mappings use exact typed `EF.Property<T>(row, "ModelName")` reads, not SQL column names.
  Indexers also use that literal typed form; arbitrary `row[name]` calls are not translated here.
- Ordinary mapped member reads in an opted-in result are rewritten to typed buffer reads too.
  Results describe the persisted scalar, not custom getter behavior. Explicit scalar helpers
  in the result expression still run normally; whole-entity helpers and navigation graphs do not.
- Nulls, binary values and configured conversions are returned in their model CLR types. The
  buffer contains all mapped scalar columns, not a pruned SQL column list or a tracked snapshot.

EF distinguishes field-only storage from tracker-owned shadow state, and supports indexer
properties separately. See [EF backing fields](https://learn.microsoft.com/en-us/ef/core/modeling/backing-field)
and [EF indexer properties](https://learn.microsoft.com/en-us/ef/core/modeling/shadow-properties#indexer-properties).

Keys and assignment targets remain ordinary CLR properties with getters/setters. Capture does
not grant field/indexer/shadow assignments, shadow/field/indexer keys, broader table mappings,
inheritance, shared/property-bag/owned roots, declared triggers, constructor-only materialization,
or additional provider dialects. Generated setter checks and direct runtime-writer validation
both preserve these restrictions. Unknown/dynamic names, wrong receivers and widened/boxed
types retain their existing diagnostics.

## Transactions and tracking

The existing native executor owns the operation transaction or uses a savepoint in an existing
one. Every returned row is materialized and projected before commit/release. A field/indexer
setter or result-mapping failure rolls back the operation; a caller-owned transaction remains
usable. Native state does not call `SaveChanges`, create tracker entries, synchronize tracked
copies, or save pending field/indexer changes. An explicit outer rollback still undoes a native
mutation that returned a result successfully.

Fixed metadata selects materialization members; no runtime service string selects a field or
indexer slot. Constructors and configured property/indexer setters execute during detached
materialization and can throw. Result scalar reads bypass entity getters, not arbitrary user
materialization behavior. Provider conversion, constraints, concurrency and transaction semantics
still need real-provider verification.

## Infrastructure capability and receipts

Custom writers additionally implement `IRepositoryReturningMappedStateWriter`, which extends
`IRepositoryReturningStateWriter`. Its `ExecuteWithMappedStateAsync<TEntity>` returns detached
`ReturningWriteRow<TEntity>` instances populated with configured mapped state and exact model
scalar values. An ordinary or shadow-only writer is insufficient (`RGD1308`); the generator
never silently fetches rows or creates a tracked fallback. Unsupported mapped shapes use `RGD1306`.

Manifests record `PerCommandIncludeMappedProperties`, each scalar's access kind (`ClrProperty`,
`FieldBacked`, `FieldOnly`, `Indexer`, `Shadow`), getter-free value mapping and unchanged key,
assignment, transaction and fixture boundaries. Narrow shadow-only receipts are unchanged.

Portable fixtures do not acquire field/indexer/shadow state from this permission alone. Complete
separately permitted scalar write maps plus SimulateImmediateWrites can explicitly simulate
[mapped returned values](../testing/INMEMORY-MAPPED-RETURNING.md), including post-update/pre-delete
timing and pre-commit mapping. Other shapes need an explicit typed override acknowledgement in strict
mode; unarranged non-strict calls fail rather than return false success. This adds no provider parity.

[Native writes](RETURNING-WRITES.md) · [Mapped-property bindings](../repositories/MAPPED-PROPERTY-BINDINGS.md) · [Write guides](README.md)
