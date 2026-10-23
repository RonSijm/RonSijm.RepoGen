# Scalar capture for native insert/upsert results

Native InsertIfAbsent and InsertOrUpdate commands can explicitly return shadow, field-backed,
field-only and indexer scalars through the existing per-command IncludeShadowProperties or
IncludeMappedProperties settings. These settings remain off by default. No new global/CLI option,
runtime property-name API or infrastructure requirement is added to simple repositories.

Start with [CapturedReservationsRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryAtomicCapture/Configuration/CapturedReservationsRepositoryDesigner.cs)
and its [exact designer → output map](../../../samples/ConfigurationExamples/MemoryAtomicCapture/README.md).
Services call the same closed writer port and receive public scalar DTOs, not EF entities or buffers.

## Configure in the designer

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Contracts")
    .UseAtomicWrites<SqliteAtomicWriter>());

entity.HasCommand("Put")
    .Create<ReservationChange>(request => new Reservation
        { Id = request.Id, Code = request.Code, Counter = request.Delta })
    .AllowWrite(row => row.Id)
    .InsertOrUpdate(row => row.Code, setters => setters
        .SetProperty<int, ReservationChange>(row => row.Counter,
            (row, request) => row.Counter + request.Delta))
    .ExpectVersion<int, ReservationChange>("Revision", request => request.ExpectedRevision)
    .Returns(row => new ReservationReceipt(row.Id, row.Code, row.Counter, row.Label,
        EF.Property<int>(row, "Revision"), EF.Property<string?>(row, "Route")))
    .IncludeMappedProperties();
```

The model supplies an ordinary unique Code, ordinary immutable primary key and approved setter
targets. In this example Label is field-backed, Revision is shadow/computed and Route is an indexer
default. Model metadata and explicit write permissions still validate every column/assignment.
Expected versions constrain existing-row updates; they are not token assignments or generation rules.

| Permission | Required atomic-writer capability | Result support |
| --- | --- | --- |
| None | IRepositoryAtomicWriter | Existing ordinary CLR results |
| IncludeShadowProperties | IRepositoryAtomicStateWriter | Ordinary scalars and true shadow values |
| IncludeMappedProperties | IRepositoryAtomicMappedStateWriter | Materializable fields/indexers as well as CLR/shadow values |

Bundled SqliteAtomicWriter and SqlServerAtomicWriter implement all three capabilities. A custom
writer must explicitly implement the corresponding interface; enabling a returning Update/Delete
writer does not select an atomic writer. Missing capabilities retain RGD1307/RGD1308 diagnostics.
Register the selected concrete atomic writer and context/lease provider in production DI as usual;
fixture registration is separate and does not replace production registration.
Turning off mapped permission leaves separately selected shadow permission intact; shadow-only
permission still rejects field/indexer mappings. Capture requires a configured scalar Returns result,
not count-only atomic writes, ReturnsMany, tracked Stage/Save or ReturnsSaved.

## Branches and values

| Outcome | Source of returned scalars |
| --- | --- |
| Inserted | Native INSERT RETURNING / OUTPUT INSERTED columns, converted to model CLR values |
| Updated | Fresh no-tracking scalar query by the immutable conflict key, inside the same operation transaction |
| AlreadyExists / NotMatched | No result value; existing or hidden rows are not exposed |

For a separate fresh DTO/navigation query inside that transaction, use the optional
[atomic ReturnsSaved mode](ATOMIC-SAVED-RESULTS.md). The scalar-buffer behavior below describes
ordinary `Returns`, not that separate query.

Upsert keeps its existing transactional insert-then-conditional-update strategy; this is not a new
single-statement upsert dialect. Conflict probing remains unfiltered. Visibility, row predicates and
expected versions constrain only the existing-row update. The updated result read retains model
filters, but not now-stale version/selection predicates or designer visibility predicates. An update
that disappears from the model-filtered result fails rather than silently bypassing that filter.
Insertion results are not filtered through a second query.

Generated infrastructure wraps exact scalar values in ReturningWriteRow. EF.Property reads use a
literal model property name and exact model CLR type. With mapped permission, ordinary mapped-member
reads also use the buffer, not custom entity getter behavior. Nullable/converted/binary values retain
their model types. The application contract does not expose EF.Property, entity types or this buffer.

All returned columns are captured; this is not SQL column pruning. Field/indexer materialization
uses the approved EF member, including nulls that replace constructor state. Entity constructors and
configured setters can execute and fail at runtime. No Entry, Attach or SaveChanges call is used.
Existing tracked entries are not flushed, synchronized or replaced.

## Transactions and restrictions

The existing atomic executor maps results before committing an owned operation transaction. Native,
materialization, cancellation or result-mapping failures roll back that owned transaction. Inside a
caller-owned transaction, the existing atomic path does not create a new operation savepoint: the
caller must roll back on failure. This differs from the Update/Delete returning executor's savepoint
policy. Successful outer rollback also undoes the operation; commit observers keep their existing
unit-of-work boundary.

Keys, conflict columns and inserted/updated assignment targets remain ordinary CLR properties.
Capture does not authorize shadow/field/indexer setters or keys. Existing unique-target, generated
column, ordinary table, constructor/materialization, duplicate-column, ownership/inheritance/shared
table, trigger and provider guards remain. No navigation result, graph materialization, arbitrary
entity helper, new dialect, automatic rowversion advancement or race guarantee is inferred.

## Optional portable fixtures

Production capture permission alone does not enable automatic fixture execution. Each writable
store must separately declare a complete public map with
MapInMemoryWrites(..., useMappedPropertyState: true), exact MapInMemoryProperty values, and explicit
named value factories where needed. The command independently opts into SimulateImmediateWrites.
Ordinary fixture primary/FK/unique identity requirements are unchanged.

The fixture evaluates saved-state conflicts, approved update conditions/original versions, explicit
insert/update factories and exact scalar results. Factories see saved rows only, not pending tracked
reservations. Copy/key/full structural round-trip/result preparation finishes before fixture commit.
Pending tracked work remains, including original-token conflicts on later shared Save. Scripts,
cancellation, partition isolation, baselines and checkpoints keep their existing behavior. Automatic
read generation may remain disabled.

Scalar model-filter simulation still requires separate per-command permission. Insertion/conflict
probes remain unfiltered; updated results apply only the declared model filter before fixture commit.
Fixture pre-commit preparation does not claim identical intermediate failure state inside a borrowed
production transaction. SQL identities, constraints, triggers, locks, races and provider rowversions
remain real-provider responsibilities.

Manifests distinguish NativeInsertBufferOrUpdatedQueryScalarBuffer, capability permission, scalar
access kinds, owned-versus-borrowed transaction policy and independent returningPropertyState
fixture receipts with providerEquivalence: false. Unconfigured generated artifacts remain unchanged.
Unsupported fixture operations keep RGD3402 and typed overrides, even for empty stores.

Actual generated SQLite comparisons cover both successful branches, hidden conflicts, nullable and
binary versions, filtered updated results, result failures and unchanged tracking. Both providers
compile in every layout; separate SQL Server cases are opt-in and do not run without their fixture
connection configuration. SQLite is not proof of SQL Server OUTPUT/locking/rowversion behavior.

[Provider capabilities](../repositories/PROVIDER-CAPABILITIES.md) · [Mapped scalar capture](RETURNING-MAPPED-VALUES.md) ·
[Atomic fixtures](../testing/INMEMORY-ATOMIC-WRITES.md) · [Mapped write state](../testing/INMEMORY-MAPPED-WRITES.md)
