# MemoryBatchResults: fresh DTOs for a bounded saved batch

Start with [BatchNotebookRepositoryDesigner](Configuration/BatchNotebookRepositoryDesigner.cs).
It configures `PatchMany(...).Save().ReturnsSaved(...)` once; callers use only
`RenameManyAsync(request)`, without a selection/projection chain.

This is a focused follow-up to [MemorySavedResults](../MemorySavedResults/README.md), which
covers single-row saves. [MemoryBatches](../MemoryBatches/README.md) covers ordinary scalar
batch results and staging. Fresh batch results are optional; none of these features is a simple-user prerequisite.

## Designer → generated output

The [generation runner](../GenerationRunner/Program.cs) pairs this designer with
[BatchNotebookContext](Model/BatchNotebookContext.cs) and emits exactly these artifacts:

| Artifact | Contents |
| --- | --- |
| [Contracts](../Contracts/Generated/BatchNotebookRepositories.g.cs) | EF-free notebook/note CQRS ports |
| [Production](Persistence/Repositories.g.cs) | Tracked patch batch + shared save + complete-key untracked projection query |
| [Test doubles](Generated/TestDoubles.g.cs) | Same named operation over public rows and one coordinated saved relationship snapshot |
| [Manifest](Generated/manifest.json) | `savedResultQuery` coverage, keys, input ordering, visibility and post-save failure boundary |

| Designer declaration | Observable result |
| --- | --- |
| `PatchMany(..., row => row.Code, ..., maximumItems: 4)` | All requested unique codes must exist before changing any row; duplicate/oversized input is rejected |
| `Where(row => row.Name == "draft")` | Selects mutation targets only; changing the name does not make the saved result disappear |
| `Save().ReturnsSaved<NotebookReceipt, RenameNotebooks>(...)` | `Task<CommandResult<IReadOnlyList<NotebookReceipt>>> RenameManyAsync(...)`, in request order |
| Composite `(TenantId, Id)` primary key | Saved-result correlation distinguishes equal local IDs; it does not reuse the single-column matching key |
| `HasInMemoryNavigation(row => row.Notes)` | Fresh notes come from declared saved tables, not pre-linked seed graphs |
| `.Where(note => !note.Archived)` inside the projection | Explicit related-row visibility; no implicit policy injection |
| Public store/read/write maps | Immutable arrangements and DTOs; consumer does not reference EF, Models or Designers |

## Run it

From this repository root, regenerate and verify the examples:

```powershell
& .\samples\ConfigurationExamples\Generate.ps1 -Configuration Release -Run -ValidateCompilation
```

[Program.cs](Program.cs) deliberately supplies `b` before `a`, stages a note through another
repository, checks the three-row shared save and nested DTOs, changes a returned collection,
and verifies baseline/checkpoint restoration and missing-target behavior.

## Boundaries

Production creates one complete-key result query for the whole batch, not one read per item.
EF translates the projection, including the nested note list. Input-order correlation happens
after materialization. Provider query splitting, limits and translation remain provider concerns.

This is a separate post-save read, not atomic OUTPUT/RETURNING. Root visibility still applies;
if a row disappears or becomes hidden after saving, the complete result fails with
`RepositorySavedResultException` rather than returning a shorter list or undoing the save.
Explicit surrounding transactions/checkpoints can still roll back. Stage/DeleteMany do not
support `ReturnsSaved`. No SQL isolation, collation, locking or race guarantee is inferred by the fake.

See [saved batch results](../../../docs/implemented/writes/SAVED-BATCH-RESULTS.md) for the full contract.
