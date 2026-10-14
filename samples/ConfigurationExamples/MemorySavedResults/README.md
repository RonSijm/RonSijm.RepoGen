# MemorySavedResults: saved commands returning related DTOs

Start with [SavedNotebookRepositoryDesigner](Configuration/SavedNotebookRepositoryDesigner.cs).
It configures `ReturnsSaved(...)` for a scalar rename, bounded note reconciliation and note creation.
[Program.cs](Program.cs) seeds public scalar rows and calls generated methods; it does not build
query chains, construct entities or run the designer.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs this designer with
[SavedNotebookContext](Model/SavedNotebookContext.cs), producing four artifacts:

| Output | Specific generated API/behavior |
| --- | --- |
| [CQRS contracts](../Contracts/Generated/SavedNotebookRepositories.g.cs) | `IWriteSavedNotebookRepository.RenameAsync` / `SaveNotesAsync`; `IWriteSavedNoteRepository.CreateNoteAsync` / `StageTextAsync` |
| [Real persistence](Persistence/Repositories.g.cs) | Saves first, then performs an untracked primary-key `Select` of the declared DTO, including its related rows; excluded from this consumer executable |
| [Test doubles](Generated/TestDoubles.g.cs) | Same ports; fresh detached snapshots of public saved stores, generated read-row relationship slots and declared key loading |
| [Manifest](Generated/manifest.json) | `savedResultQuery` coverage receipt: complete keys, saved state, visibility, relationships and post-save failure boundary |

| Designer declaration | What changes in output |
| --- | --- |
| `Save().ReturnsSaved(row => new NotebookReceipt(...))` | `CommandResult<NotebookReceipt>` from a **fresh saved read**, not the tracked entity's current navigation graph |
| `Notes.Where(!Archived).OrderBy(Code).Select(...).ToList()` inside the result | Explicit related visibility, ordering and nested DTOs; no automatic child-policy injection |
| `ReconcileCollection(...).SimulateCollectionWrites()` | The result sees saved additions, edits and deletions, using the same approved production rules |
| `HasInMemoryNavigation(row => row.Notes)` / `row.Notebook` | Complete mapped relationship keys load collection/reference slots from separate tables, never seed graphs |
| `MapInMemoryEntity` / `MapInMemoryWrites` | Internal entity types remain absent from consumer dependencies; the generator emits plain scalar read rows |
| Child `StageText` followed by parent `Rename` | The parent result also sees the child change saved by the shared unit of work |
| `CreateNote...ReturnsSaved(...)` | Newly allocated ID and the existing notebook's saved name are projected after insertion |

## Run and inspect

```powershell
dotnet run --project samples/ConfigurationExamples/MemorySavedResults -c Release
```

The executable validates nested results after shared save and reconciliation, archived-row exclusion,
generated IDs/reference results, checkpoint rollback and baseline restoration. It depends only on
Contracts, Testing and DI—not EF, Models, Designers or the generator. Regenerate with
[Generate.ps1](../Generate.ps1).

`ReturnsSaved` is explicit and optional. Ordinary `Returns` keeps mapping the saved tracked scalar
row and does not add a query. `Stage`, deletes, batches, immediate writes and `ReloadAfterSave` cannot
be combined with the new option. A failed result query raises `RepositorySavedResultException`:
the write is already saved, although a surrounding transaction can still roll it back. This separate
read is not an atomic SQL returning statement or a provider race guarantee.

See the [saved-result contract](../../../docs/implemented/writes/SAVED-COMMAND-RESULTS.md) for visibility, complete keys,
fixture coverage boundaries, unsupported relationships and failure semantics.
