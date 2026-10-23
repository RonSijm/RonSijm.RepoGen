# Fresh saved results for bounded patch batches

This complete-primary-key correlation also supports saving [CreateMany](CREATE-MANY-COMMANDS.md)
commands: generated keys are captured after saving, one fresh query covers all new rows, and DTOs
follow input order. There is no pre-insertion matching-key query. See
[MemoryCreateBatches](../../../samples/ConfigurationExamples/MemoryCreateBatches/README.md).

`PatchMany(...).Save().ReturnsSaved(...)` is an optional designer configuration that returns
fresh scalar/DTO values, including projected related data, for every requested row. The service
calls one named operation. It does not supply a projection, assemble a query or depend on EF.

```csharp
entity.HasCommand("RenameMany")
    .PatchMany<RenameItem, string, RenameRequest>(
        request => request.Items,
        row => row.Code,
        item => item.Code,
        setters => setters.SetProperty(row => row.Name, (row, item) => item.Name),
        maximumItems: 100)
    .Save()
    .ReturnsSaved<NotebookReceipt, RenameRequest>((row, request) => new NotebookReceipt(
        row.Id, row.Name, request.Label,
        row.Notes.Where(note => !note.Archived).OrderBy(note => note.Id)
            .Select(note => note.Text).ToList()));
```

The generated method returns `Task<CommandResult<IReadOnlyList<NotebookReceipt>>>`.
Use the one-parameter `ReturnsSaved(row => ...)` overload when no request data is needed.
`ReturnsMany` still means native immediate returning mutations, not a tracked batch result.

## Save and result boundaries

| Phase | Behavior |
| --- | --- |
| Input | Existing finite limit, null/duplicate validation and input ordering apply |
| Target preparation | Every visible matching target must exist; per-item versions/setters retain normal batch semantics |
| No save outcome | Empty/unassigned, missing or conflicting batches do not execute the result query |
| Shared save | Saves other repositories' staging too; `AffectedRows` is the shared save count, not input count |
| Fresh read | Uses every saved primary-key part, not matching-key inference or the mutation predicate |
| Projection | One untracked complete-key query for the batch; EF executes the configured projection |
| Correlation | Materialized results are indexed by complete structural keys, then returned in input order |
| Missing result | Missing/hidden/duplicate results fail the whole result; never silently return a shorter list |
| Result failure/cancellation | `RepositorySavedResultException`: the save already succeeded; automatic rollback/retry is not implied |

Production builds a balanced OR of generated complete-key comparisons, with no reflection-based
field lookup or client-side predicate fallback. Scalar and composite primary keys are supported
when all parts are public readable CLR properties. Structural correlation handles binary key parts.
Batch **matching** still requires its existing single non-null unique scalar; this does not add
composite matching or allow changing keys. Visibility policies are rechecked unless explicitly
bypassed; related-row visibility stays explicit in the projection.

The read is separate from the save, not an atomic SQL returning statement. Concurrent external
changes can affect the result. An explicitly surrounding transaction retains the existing batch
savepoint rules and can cover both operations. Provider limits, collation, query splitting and
translation remain provider responsibilities; no transparent chunking or extra dialect is introduced.
One result query does not promise one SQL statement when a provider deliberately splits a projection.

RGD3701 rejects Stage/DeleteMany/immediate operations, absent explicit Save, redundant
ReloadAfterSave and unreadable/incomplete CLR keys. The normal RGD3001/RGD3002 batch restrictions
still apply. Ordinary `Returns` keeps its direct scalar saved-row mapper and rejects graph results.
Choosing a later `Returns` switches back to that mode. Default generated output stays unchanged.

## Optional in-memory execution

Existing `UseInMemoryTestStores()` and `UseInMemoryTestCommands()` enable suitable batch handlers.
Fresh scalar results do not require enabling query generation. Related DTOs additionally need
`UseInMemoryTestQueries()`, selected stores and explicit `HasInMemoryNavigation` declarations.
Public scalar read/write mappings support internal models without constructing them in the fake.

After shared save the result callback runs once, outside the write lock. It captures participating
tables together in the selected partition, loads declared detached relationship slots and correlates
the saved inputs by complete keys. All items use that one snapshot; shared staged edits and explicit
save-time generated values are visible. Returned lists cannot alter stored arrangements.
Scripts still take precedence, and baseline/checkpoint restoration still works after saving or a
result failure. This does not add SQL constraints, lock/isolation/race guarantees or support for
arbitrary graphs, navigation-dependent writes, owned/skip relationships or provider-only expressions.
Unsupported shapes retain explicit overrides/strict RGD3402 diagnostics.

Supported batch commands add `batchOrder` and `batchCapture` to their `savedResultQuery` manifest
receipt. Those fields are absent for ordinary batches and single-row saved results.

## Focused designer example and checks

[MemoryBatchResults](../../../samples/ConfigurationExamples/MemoryBatchResults/README.md) pairs
[BatchNotebookRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryBatchResults/Configuration/BatchNotebookRepositoryDesigner.cs)
with four actual artifacts and an EF/model-free consumer. It deliberately requests `b` before `a`,
uses notebooks with equal local IDs in different composite keys, stages a note through another
repository, excludes archived notes and checks missing targets and rollback.

Tests execute actual generated SQLite and mapped in-memory repositories from the same designer.
A SQL-command capture verifies one target load plus one nested result read for two items. Runtime
cases check input order, complete structural keys, missing/duplicate result detection, callback
cardinality, pre-save guards, cancellation and committed-result failures. These checks do not
establish equivalence with every database/provider.

See [ordinary batches](BATCH-COMMANDS.md), [fixture batches](../testing/INMEMORY-BATCHES.md) and
[single-row saved results](SAVED-COMMAND-RESULTS.md).
