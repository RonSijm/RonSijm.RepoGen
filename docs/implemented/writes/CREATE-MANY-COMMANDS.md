# Bounded designer-defined creation

`CreateMany` maps request items to new scalar rows through one named closed operation. It complements
[matching-key patch/delete batches](BATCH-COMMANDS.md): new rows do not need artificial selection
keys. It is optional, works with simple defaults, and does not introduce a native bulk-insert provider.

```csharp
entity.HasCommand("CreateEntries")
    .CreateMany<NewEntry, CreateEntryBatch>(
        request => request.Items,
        item => new IntakeEntry { FolderId = item.FolderId, Title = item.Title.Trim() },
        maximumItems: 5)
    .AllowWrite(row => row.FolderId)
    .Save()
    .ReturnsSaved<CreatedEntry, CreateEntryBatch>((row, request) =>
        new CreatedEntry(row.Id, row.Title,
            row.Folder == null ? "missing" : row.Folder.Name, request.Label));
```

This generates `Task<CommandResult<IReadOnlyList<CreatedEntry>>> CreateEntriesAsync(CreateEntryBatch,
CancellationToken)`. The mapping belongs to the designer, not another service argument.
`Returns(row => row.Id)` instead returns input-ordered generated IDs through the same list wrapper.
Omit the projection for `Task<CommandResult>`; choose `Stage()` for pending changes with no saved
result. [MemoryCreateBatches](../../../samples/ConfigurationExamples/MemoryCreateBatches/README.md)
links the designer to exactly four generated artifacts and an EF-free consumer.

## Preparation, persistence and results

| Choice or condition | Behavior |
| --- | --- |
| No declaration | No create-batch method, dependency or receipt |
| Limit omitted | At most 1,000 items; choose a smaller application/provider-appropriate bound |
| Empty collection | `NoChanges` before context acquisition/fixture selection; no save |
| Null collection/item, excess items or input cancellation | Rejected before context acquisition |
| Enumeration | Once, bounded; preserves order and item references, not deep copies |
| Mapping | Every candidate is prepared before any batch row is attached/staged |
| `Stage()` | All prepared rows remain pending; no result projection or save |
| `Save()` | One shared save, including unrelated staged work; affected count covers that save |
| Ordinary `Returns` | Saved tracked scalars in input order |
| `ReturnsSaved` | One fresh complete-primary-key projection query for all created rows, in input order |

`Save()` is not a promise of one SQL statement or roundtrip. EF batching/value generation and the
provider determine execution. Existing-row filters, update setters, expected versions, reconciliation,
upsert/returning modes and reload are different command shapes, not implicit create-batch options.
`AllowWrite` and property protection remain command-local. Foreign keys are explicitly mapped;
ownership and graph construction are never guessed from property names.

Production save requires a relational provider with automatic transactions enabled. Existing
transactions need supported, enabled savepoints; ambient `TransactionScope` is rejected. Mapping,
attachment, cancellation and ordinary save failures remove only this batch's staged rows, preserving
earlier pending work. Cleanup removes new relationship-fixup references and detects reverted generated
keys before detachment, preventing failed rows from being reattached or retained in the identity map
on retry. Unrelated shared-save concurrency failures become `RepositoryWriteConflictException`, not
invented create conflicts. Provider exceptions retain their failure details.

After a successful save, projection/read failure or cancellation throws `RepositorySavedResultException`:
the write has already been saved. Do not blindly retry creation. Explicit transaction rollback remains
available. Commit-notification failures retain `RepositoryCommitException` semantics; they do not undo
committed inserts.

Fresh results reuse [saved batch correlation](SAVED-BATCH-RESULTS.md), including complete composite
primary keys, request bindings and root visibility. Create visibility does not select or authorize
insertion; result visibility applies only to the fresh read. An explicit `BypassVisibility` is allowed
for a saved-result create and only changes that read. Related visibility stays explicit in the projection.

## Optional fixtures and boundaries

`UseInMemoryTestCommands()` enables supported create batches through the existing optional fixture
backend. Without it, typed scripts remain available and production/contracts are unchanged. No new
global switch is required. `RequirePortableTestCommands()` rejects unsupported expressions unless
the command deliberately declares `RequireTestOverride(reason)`.

Both entity-shaped and explicitly mapped public-data stores are supported. Generated fixture values
require pure `HasInMemoryGeneratedValue` factories. Allocation sees saved rows, earlier pending rows
and earlier candidates in this batch, without prematurely staging candidates. Complete fixture keys
use the configured store comparer. Failed preparation consumes no row-derived reservations. Shared
saves, baseline reset and checkpoint rollback retain their existing semantics. Saved DTOs reuse one
coordinated participating-store snapshot and declared navigation edges.

The fake does not simulate SQL unique/FK constraints, provider identities, locks, triggers, collation
or isolation. Public-data mappings do not execute persistence constructors; entity-shaped factories do.
Constructor/helper/interceptor bodies are not inspected sandboxes. All adapters must be pure; do not
mutate input items during an operation.

Creation is scalar on ordinary non-owned, non-shared, non-inherited entities. Designer navigation
assignments are rejected during generation; populated navigation graphs are rejected in production
(initialized empty collections are permitted). Nested/owned graph writes, many-to-many insertion,
native high-volume bulk import and cross-database atomicity remain separate extensions.
