# Optional bounded fixture batches

`UseInMemoryTestCommands()` also implements suitable designed `PatchMany` and leaf `DeleteMany`
operations. There is no extra service-side batch API or generation flag: configure the production
operation once in a RepositoryDesigner. Automatic test writes remain **off by default**.

Optional [CreateMany](../writes/CREATE-MANY-COMMANDS.md) uses the same backend for new scalar rows without
matching keys: all-candidate preparation, reservation-aware generated IDs and input-ordered results.
See [MemoryCreateBatches](../../../samples/ConfigurationExamples/MemoryCreateBatches/README.md).

Start with [BatchWorkspaceRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryBatches/Configuration/BatchWorkspaceRepositoryDesigner.cs)
and its [designer → exact output map](../../../samples/ConfigurationExamples/MemoryBatches/README.md).
[Production batch commands](../writes/BATCH-COMMANDS.md) explain the relational implementation and limitations.

## One definition, two implementations

```csharp
entity.HasCommand("UpdateTitles")
    .Where<BatchDocumentsRequest>((row, request) => row.OwnerId == request.OwnerId)
    .PatchMany<BatchDocumentChange, string, BatchDocumentsRequest>(
        request => request.Items, row => row.Code, item => item.Code,
        setters => setters
            .SetPropertyWhen(row => row.Title, (row, item) => item.Title.Value, item => item.Title.IsSpecified)
            .SetProperty(row => row.Version, row => row.Version + 1)
            .ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion),
        maximumItems: 4)
    .AllowWrite(row => row.Version).Save()
    .Returns(row => new BatchDocumentCard(row.Id, row.Code, row.Title, row.Version));
```

The fixture uses the same root predicates, visibility/bypasses, request guards, optional assignments,
expected versions, approved fields, context-key selector and result mappings. Unique scalar matching
may use a different column from the complete primary key selected by `HasInMemoryStore`.
There is no reflection-based setter or dynamic key inference.

Input preparation is shared by production and fixture implementations through EF-free Abstractions.
It enumerates once, preserves input order, enforces the configured finite limit and rejects null
collections/items/keys and duplicate keys before staging. It snapshots keys and item references,
not arbitrary mutable item contents; callers must not mutate requests during execution. Cancellation
is checked during enumeration. Empty input returns `NoChanges` without selecting a context partition.

## All targets first, then stage or save

- Every supplied key must match a saved, visible row. One missing/hidden/other-owner row returns
  `NotFound`; a stale expected token returns `Conflict`. Neither partially stages earlier items.
- All patches run on detached candidate rows before publishing pending state. Setter/copy failures,
  attempted primary/matching-key mutation and preparation cancellation leave existing state alone.
- Target rows with pending modifications must be saved/discarded first, matching the production
  batch safeguard. Unrelated staged rows remain untouched until a shared Save.
- `.Stage()` returns `Staged` with no saved value; reads still see saved rows. `.Save()` commits all
  pending repositories in the selected partition, and `AffectedRows` is that shared save count.
  Saving another typed context partition never commits this one.
- No supplied assignments returns `NoChanges`. Without expected tokens an unchanged assignment
  can be `Applied` with zero affected rows. Declared expected versions force a conditional write;
  version increments remain explicit designer setters, not inferred fixture behavior.
- Saved result DTOs follow input order, including untouched items when another item was assigned.
  Delete results use pre-deletion state. Results are detached before Save and mapped afterwards;
  mapping failure raises `RepositorySavedResultException`, not an unsaved-write failure.
  Optional [ReturnsSaved](../writes/SAVED-BATCH-RESULTS.md) on a saving PatchMany instead captures one coordinated
  saved-table snapshot for all inputs and can project declared related DTOs in input order.
- Shared Save prepares every store before committing any. Batch failure restores its target pending
  state; a conflicting unrelated staged entry remains available for explicit resolution and raises
  `RepositoryWriteConflictException` rather than pretending the batch itself was stale.

The existing [checkpoint transaction](INMEMORY-WRITES.md#checkpoint-transactions) can roll back saved
batches across the partition. Commit does not implicitly Save. Scripts override automatic execution;
`ResetTestDouble` resets scripts/history, not saved data or pending writes.

## Conservative capability boundary

All [scalar-write prerequisites](INMEMORY-WRITES.md#capability-boundary-and-coverage) still apply:
public entity-shaped or [explicitly scalar/write-mapped stores](INMEMORY-PROXY-WRITES.md), complete scalar copies and primary keys, ordinary CLR types,
public entity-shaped setters, portable expressions and explicit ordinary concurrency tokens. The production model
must declare a direct unique scalar batch matching key; fixture matching uses ordinary CLR equality,
not provider collation or custom conversions/comparers. Seeded duplicate values in that matching
column fail rather than choose an arbitrary row; the fixture does not enforce SQL unique constraints.

Separate [mapped scalar write-state consent](INMEMORY-MAPPED-WRITES.md) additionally permits exact
named scalar/token bindings for these root-row batches, including explicitly designed
[named PatchMany setters](../writes/MAPPED-BATCH-SETTERS.md). Matching/primary keys stay ordinary
and immutable. Named targets cannot change foreign/unique identity; existing member-based write
permissions remain unchanged. This does not grant named targets to child/association reconciliation.
The separate [mapped reconciliation setter capability](../writes/MAPPED-RECONCILIATION-SETTERS.md)
has its own scoped child grants and existing owner/leaf/link guards.
The [MappedBatchSetters example](../../../samples/ConfigurationExamples/MappedBatchSetters/README.md)
shows the complete designer-to-output mapping and an EF/model-free consumer.

Updates/deletes may operate on **known existing generated-on-insert values**, including integer
primary keys. They do not require the fixture to generate anything. Create and update generation require
[explicit scalar factories](INMEMORY-GENERATED-VALUES.md); known generated-token deletes need none.
Database rowversion/race equivalence is not promised.
ReloadAfterSave, native/set-based/atomic writes, graphs/cascades and inferred proxy reverse mappings remain
override or real-provider cases. No SQL isolation, locking or race guarantees are inferred.

`RequirePortableTestCommands()` rejects uncovered operations with RGD3402 unless a command has
`RequireTestOverride(reason)`. The manifest records per-operation coverage and `writeSimulation`
batch matching, target-state and save semantics. Turning automatic writes off leaves production
contracts, implementation, API snapshots and cache fingerprints unchanged.

Shared tests run actual generated fixture and SQLite repositories through the same named operations,
including bounded input failures, visibility/version failures, optional assignments, Stage/shared Save,
ordered results and rollback. These checks supplement [real-provider contract fixtures](REPOSITORY-CONTRACT-FIXTURES.md);
they do not establish equivalence with every database or provider.
