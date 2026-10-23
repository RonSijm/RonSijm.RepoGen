# Explicit mapped scalar returned values in fixtures

Native Update/Delete commands using IncludeShadowProperties or IncludeMappedProperties can reuse
complete explicitly permitted [mapped scalar fixture write state](INMEMORY-MAPPED-WRITES.md).
This is optional fixture simulation, not an implementation of native returned buffers or EF's tracker.
No new global switch, CLI option or service-side property/projection API is added.

Separate [atomic insert/upsert scalar capture](../writes/ATOMIC-SCALAR-CAPTURE.md) composes the same
independent complete-state permission with atomic-writer capabilities and existing saved-only
insert/update simulation. It retains its own filtered updated read and borrowed-transaction policy.

Start with [ReceiptQueueRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryMappedReturning/Configuration/ReceiptQueueRepositoryDesigner.cs)
and its [exact designer → output map](../../../samples/ConfigurationExamples/MemoryMappedReturning/README.md).
Its one-table field-backed/shadow model generates both production operations and an EF/model-free
consumer. The consumer gets stored scalar values, not the entity's formatted display getter.

## Three independent permissions

1. Configure a complete public store, MapInMemoryEntity, exact MapInMemoryProperty bindings and
   MapInMemoryWrites(..., useMappedPropertyState: true). Ordinary CLR assignments can supply ordinary
   scalars. Every EF scalar must be represented; identity/FK/unique members retain ordinary assignments.
2. Configure a production returning writer and per-command IncludeShadowProperties or
   IncludeMappedProperties, following their existing production shape/writer restrictions.
3. Enable automatic fixture commands and per-command SimulateImmediateWrites. Automatic fixture
   reads are independent and may remain disabled. Strict coverage is recommended.

```csharp
entity.HasInMemoryStore<NoteSeed, int>(row => row.Id,
        row => new(row.Id, row.Title, row.Counter, row.Revision))
    .MapInMemoryEntity<NoteSeed>(row => new StoredNote
        { Id = row.Id, Title = row.Title, Counter = row.Counter })
    .MapInMemoryProperty<NoteSeed, int>("Revision", row => row.Revision)
    .MapInMemoryWrites<NoteSeed>((row, saved) => new(row.Id, row.Title, row.Counter,
        EF.Property<int>(row, "Revision")), useMappedPropertyState: true)
    .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Counter + 1);

entity.HasCommand("Advance")
    .Where<NoteRequest>((row, request) => row.Id == request.Id)
    .ExpectVersion<int, NoteRequest>("Revision", request => request.ExpectedRevision)
    .Update(setters => setters.SetProperty(row => row.Counter, row => row.Counter + 1))
    .ReturnsMany(row => new NoteReceipt(row.Id, row.Title, EF.Property<int>(row, "Revision")))
    .IncludeShadowProperties() // Use IncludeMappedProperties for a field/indexer model.
    .SimulateImmediateWrites();
```

Neither capture permission nor a read binding grants writes. Complete ordinary maps with explicit
write-state consent are also valid even when no named slots are needed. Required values/factories,
exact property names/types and unsupported expression guards remain independently validated.
Defaults and existing unconfigured output remain unchanged.

## Results and write boundaries

| Operation | Returned state | Generated values |
| --- | --- | --- |
| Update + ReturnsMany | Matched post-update scalar rows | Only explicit configured update factories |
| Delete + ReturnsMany | Matched original/pre-delete scalar rows | No factories run |
| Zero matches | Empty MutationResult, AffectedRows = 0 | No absence-versus-version-conflict inference |

Selection uses current saved rows, designed visibility/predicates and exact scalar/nullable/binary
expected versions. Opted-in model filters still need their own per-command permission. They constrain
selection, not a fresh result query: an update can return a matched row whose new values fail the
filter. Setters read original rows. Returned order is unspecified; compare keyed/sorted values in tests.

Typed result expressions lower from the original designer projection to safe scalar fixture slots.
No entity constructor/getter/indexer, EF.Property call, tracker entry or native buffer is used by the
generated fixture. Literal exact typed EF.Property reads need a declared binding; ordinary member
reads can use complete ordinary assignments. Arbitrary/dynamic names do not become runtime lookups.
Explicit DTO constructors/helpers still execute and can throw.

All targets, setters, factories, structural copy/read-write-read validation and results are prepared
before any rows commit. A mapping failure or pre-commit cancellation cannot partly update/consume
rows. Explicit copy recipes remain responsible for mutable data; binary write-state comparisons and
detached scalar snapshots retain their existing structural/copy policies.

Immediate execution does not save or refresh pending tracked work. Staged inserts are not targets;
staged original tokens remain unchanged and can conflict on a later shared Save. Checkpoint rollback
and baseline restore include mapped scalar state. Script/handler precedence is unchanged, including
RequireTestOverride acknowledgement and strict unarranged failures on empty stores.

## Coverage, production and limitations

Supported operations report Generated plus returningPropertyState: explicit scalar state rather than
a native buffer, post-update/pre-delete timing, factories only on update, preparation before commit,
pending isolation, unspecified ordering and providerEquivalence: false. The production capture receipt
records fixtureSimulation only when this independently validated operation is generated.

Disabling either opt-in retains RequiresOverride; strict unacknowledged commands fail RGD3402.
Invalid/incomplete maps still fail RGD3601. Production writer capability, table/constructor/member,
ordinary key/setter, projection and result-nullability checks are not weakened by fixture consent.
Production source, contracts, API/cache shapes and consumer dependencies do not change.

This does not simulate native buffers, SQL conversion/collation/null propagation, generated identity,
database rowversion, triggers, constraints, locks, races, durability, owned/shared/inherited graphs,
cascades or graph reconciliation. Unsupported shapes need typed scripts or real-provider fixtures.
Actual generated SQLite comparison tests cover computed shadows, getter-free fields, indexers,
nullable/binary values, model-filter selection and mapping rollback; they do not establish SQL Server
OUTPUT/rowversion/provider equivalence.

[Immediate writes](INMEMORY-IMMEDIATE-WRITES.md) · [Mapped state](INMEMORY-MAPPED-WRITES.md) ·
[Production mapped capture](../writes/RETURNING-MAPPED-VALUES.md) · [Testing index](README.md)
