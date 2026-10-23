# BatchWrites: one designed operation for many explicit keys

Start with [BatchNotesRepositoryDesigner](Configuration/BatchNotesRepositoryDesigner.cs).
It chooses a simple borrowed-context setup and explicitly declares small, owner-scoped batch operations.
[Program.cs](Program.cs) is only a runtime consumer/demo; it never runs the designer.

## Designer → generated output

| Designer declaration | Generated API | Implementation behavior |
| --- | --- | --- |
| `HasQuery("ListNotes")...ProjectTo(...).List()` | `IReadBatchNoteRepository.ListNotesAsync(ownerId)` | Owner-filtered, sorted DTO read; no consumer tracking/LINQ controls |
| `HasCommand("RenameNotes")...PatchMany(...).Save().Returns(...)` | `IWriteBatchNoteRepository.RenameNotesAsync(request)` | Bounded key query, per-item versions and approved partial setters, one shared save, input-ordered DTO list |
| `HasCommand("StageRenames")...PatchMany(...).Stage()` | `IWriteBatchNoteRepository.StageRenamesAsync(request)` | Same mapping, but changes stay pending; no saved-value result |
| `HasCommand("DeleteNotes")...DeleteMany(...).Save().Returns(...)` | `IWriteBatchNoteRepository.DeleteNotesAsync(request)` | Tracked leaf deletion with an original version for every item; shared save and deleted scalar DTOs |

Find those exact symbols in the [generated contracts](../Contracts/Generated/BatchWritesRepositories.g.cs)
and [generated implementation](Generated/Repositories.g.cs). The [manifest](Generated/manifest.json)
records the batch key, limit, setters, versions and save/result policies. Persistence selectors remain internal.

The [GenerationRunner](../GenerationRunner/Program.cs) explicitly pairs the designer with
[BatchWritesContext](Model/BatchWritesContext.cs) and chooses these three files. Filenames are not
inferred from the designer's class name. Handwritten contracts live in [Contracts](Contracts/NoteEdit.cs).
The model, designer and EF-free contract projects compile linked inputs; the consumer references no designer.

## The important declaration

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

The Code has a unique EF index. Each item has its own expected version and an `Optional<string?>`
title: omitted preserves the title, supplied null clears it. The unconditional version setter deliberately
advances the version even when the title was omitted, ensuring a conditional write for every matched item.
OwnerId is explicitly protected and never assigned by these mappings; the owner predicate determines scope.
The application still authorizes the caller and supplies a trustworthy OwnerId.

The consumer simply calls:

```csharp
var result = await writes.RenameNotesAsync(request, cancellationToken);
```

Its return type is `CommandResult<IReadOnlyList<NoteSummary>>`; `Returns` configures one row and RepoGen
adds the list. Requests are snapshotted before context acquisition; empty input does nothing, while null,
duplicate or oversized input fails. One missing/hidden/other-owner key returns NotFound for the whole batch.
One stale item returns Conflict and restores earlier item changes. No item is silently skipped.

## What the executable checks

- Two saved edits return DTOs in input order, including a supplied-null title and current versions.
- A stale second item cannot leave the first item's title changed.
- Mixed-owner inputs cannot partially change the allowed owner either.
- Stage changes remain absent from a normal read until a later save.
- Saving a disjoint deletion also saves the earlier staged patch on the shared context.
- The other owner's row remains unchanged.

One Save is not one SQL statement/roundtrip. `AffectedRows` is the shared save count, so the final
delete reports two changes: its own deletion plus the earlier staged patch. This is not native bulk DML,
cascade graph deletion, a bulk-import engine or a service-level transaction across separate calls.

## Optional by design

Nothing new is generated without PatchMany/DeleteMany. The example needs no tenant resolver, cache,
async context provider, native writer or unit-of-work registration. The simple preset remains enough;
its generic range-helper flag does not remove explicitly designed commands.

See [batch command policies and failure boundaries](../../../docs/implemented/writes/BATCH-COMMANDS.md) for supported
keys, limits, scalar-only mappings, transaction safeguards and precise shared-save semantics.

## Run

From the repository root:

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Run -ValidateCompilation
dotnet run --project samples/ConfigurationExamples/BatchWrites -c Release
```

Generation uses a separate runner, so a clean first generation does not need this consumer's output.
