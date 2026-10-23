# Optional designer-defined batch commands

`PatchMany` and `DeleteMany` configure bounded **tracked** commands over explicitly supplied keys.
Services call one named method with an application request, not a loop of selector methods or EF entities.
The [BatchWrites example](../../../samples/ConfigurationExamples/BatchWrites/README.md) maps each designer
declaration directly to its generated API and runnable consumer.

For new rows, use optional [CreateMany](CREATE-MANY-COMMANDS.md) instead: one bounded item mapping,
no artificial matching key, the same explicit Stage/shared Save and input-order result semantics.
It does not turn this tracked API into native bulk insertion.

## Declare the operation once

```csharp
entity.HasCommand("RenameNotes")
    .Where<EditNotesRequest>((note, request) => note.OwnerId == request.OwnerId)
    .PatchMany<NoteEdit, string, EditNotesRequest>(
        request => request.Items,
        note => note.Code,
        item => item.Code,
        items => items
            .SetPropertyWhen(note => note.Title, (note, item) => item.Title.Value,
                item => item.Title.IsSpecified)
            .SetProperty(note => note.Version, note => note.Version + 1)
            .ExpectVersion(note => note.Version, (note, item) => item.ExpectedVersion),
        maximumItems: 20)
    .AllowWrite(note => note.Version)
    .Save()
    .Returns(note => new NoteSummary(note.Code, note.Title, note.Version));
```

This generates `Task<CommandResult<IReadOnlyList<NoteSummary>>> RenameNotesAsync(EditNotesRequest,
CancellationToken)`. `Returns` describes one saved row; RepoGen supplies the list wrapper. Results
follow **input order**, not unspecified database ordering. A result mapping can also bind the outer
request through the existing `Returns<TResult, TRequest>` overload, but not an extra per-item public argument.

Use `DeleteMany` with the same item/key inputs and an item builder containing only expected versions.
Use `Stage()` instead of `Save()` to leave changes pending. Staged operations cannot return saved
values. With no `Returns`, the contract is `Task<CommandResult>`.

## Defaults and boundaries

| Configuration | Behavior |
| --- | --- |
| No batch declaration | No new methods, constructor dependencies or generated output |
| `maximumItems` omitted | At most 1,000 input items; choose a provider-appropriate smaller limit if needed |
| Empty input | `NoChanges`, before context acquisition; no query or save |
| Null input/item/key or duplicate key | Input error before context acquisition |
| One missing, hidden or differently owned key | Whole operation returns `NotFound`; no partial application |
| One stale expected version | Whole operation returns `Conflict`; affected tracker changes are restored |
| `Save()` | One shared `SaveChangesAsync` call, not a promise of one SQL statement/roundtrip |
| `Stage()` | No save; a later shared save persists these and other pending tracked changes |
| Pending edits on a target row | Rejected before mutation; unrelated pending edits remain intact |

These declarations require closed repositories. The `GenerateBatchOperations` switch controls generic
selector range helpers, not explicitly named designer commands; using `UseSimpleDefaults()` does not
silently remove a command the designer deliberately declares. No new CLI switch or mandatory cache,
tenant resolver, context lease, native writer or unit-of-work service is introduced.

Matching requires one non-null mapped scalar with a declared primary/alternate key or unfiltered
unique index. Composite/array keys, owned/shared entities and inheritance are not supported here.
Keys use default CLR equality; if database collation/conversion gives them different equality semantics,
design a normalized input key or use an explicit adapter. A mismatch fails closed rather than selecting
a different row. Keys and ownership are never inferred from property names.

Batch patches assign only declared settable scalars. They cannot change the matching key, immutable
keys or read-only/generated columns. Existing `AllowWrite`, [ProtectWrite](WRITE-PROTECTION.md),
`Optional<T>` and concurrency metadata rules apply. Permissions belong to the outer named command,
not every batch automatically. Per-item versions come from the item, never the freshly loaded row.
Versioned patches require an unconditional setter so every matched item performs a conditional write.

`DeleteMany` supports leaf rows without referencing dependents. It does not infer cascade/graph deletion.
Setters and ordinary `Returns` projections cannot traverse entity navigations. Optional
[ReturnsSaved](SAVED-BATCH-RESULTS.md) on a saving PatchMany projects fresh related DTOs in one
complete-key result query for the batch and preserves input order; DeleteMany/Stage exclude this mode.
Implicit lazy loading is temporarily disabled during execution and its exact previous setting is restored.
Arbitrary helper/constructor/interceptor bodies are application responsibilities, not inspected sandboxes.
The input snapshot preserves item references, not deep clones: do not mutate requests during execution.

## Persistence and failure semantics

One bounded, filtered key query loads the target rows; this is not one query per item. EF global filters
and designed visibility/ownership predicates constrain that query even when hidden entities are already
tracked. Request values remain SQL parameters. EF/provider translation and parameter limits still apply;
there is no client-side filtering, transparent input chunking or native bulk-import fallback.

Saving requires a relational provider and enabled automatic transactions. An existing context transaction
must support enabled savepoints; an ambient `TransactionScope` is rejected. These checks fail before
target loading/mutation. This deliberately retains EF's normal transaction protection rather than allowing
partial batch persistence when it has been disabled. See [EF transaction behavior](https://learn.microsoft.com/en-us/ef/core/saving/transactions).
The batch does not create an enclosing transaction across multiple service operations: use the existing
explicit unit of work when that is needed. Stage defers persistence safeguards to the later save.

Pre-save mapping/cancellation failures and target concurrency conflicts restore all batch scalar values,
original values and modification flags, preserving unrelated pending edits. A shared-save conflict on a
different tracked operation raises `RepositoryWriteConflictException`, not a misattributed batch Conflict.
No stale command is retried automatically.

`AffectedRows` remains the shared save count, not the number of requested items. A save may also persist
other staged entities. Save participants and cache invalidation retain their existing commit boundaries;
staging does not invalidate cached reads. A saved-result failure raises `RepositorySavedResultException`
because persistence already succeeded; do not blindly retry the write. Post-commit failures are not
reported as rollback. Database defaults/triggers, authorization and domain validation remain application-owned.

## Inventory and diagnostics

Optional manifests include the item type, matching property, limit, assignment/version names, missing-key
outcome, shared-save scope and result order. Contracts remain EF-free, including bound application ports,
nullability, split outputs and API/data snapshots.

- `RGD3001`: unsupported/mixed batch operation, surface, matching key, limit or graph deletion.
- `RGD3002`: invalid/repeated scalar assignment, token binding or entity-navigation result/mapping.

Existing closed-contract, mapped-property and write-protection diagnostics also apply. Invalid configuration
fails before generated files are published. This is bounded request-to-row mapping, not a bulk-copy engine,
collection-replacement policy, authorization check or business-rule interpreter.

## Optional fixture implementation

Explicit [named mapped item setters](MAPPED-BATCH-SETTERS.md) additionally support exact non-identity
field/shadow/indexer targets through tracked entry state. Identity/generated/graph guards remain;
fixture state needs its own complete public-data write-map permission. See the focused
[MappedBatchSetters designer](../../../samples/ConfigurationExamples/MappedBatchSetters/README.md).

`UseInMemoryTestCommands()` supports suitable public scalar PatchMany and leaf DeleteMany operations
without EF in the test consumer. The same designer definition supplies filters, approved setters,
expected versions, bounded input, Stage/Save and ordered results. This is a fixture simulation, not
SQL constraint/isolation/race equivalence; unsupported shapes require explicit scripts or real-provider
tests. See [fixture batch semantics](../testing/INMEMORY-BATCHES.md) and the focused
[MemoryBatches RepositoryDesigner example](../../../samples/ConfigurationExamples/MemoryBatches/README.md).
