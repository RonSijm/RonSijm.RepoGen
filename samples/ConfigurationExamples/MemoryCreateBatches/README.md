# MemoryCreateBatches: one designed create operation, many new rows

Start with [IntakeCatalogRepositoryDesigner](Configuration/IntakeCatalogRepositoryDesigner.cs).
It consumes [IntakeCatalogContext](Model/IntakeCatalogContext.cs) and emits exactly these artifacts:

| Output | Meaning |
| --- | --- |
| [IntakeCatalogRepositories.g.cs](../Contracts/Generated/IntakeCatalogRepositories.g.cs) | EF-free CQRS contracts; named creation/staging methods take an application request |
| [Persistence/Repositories.g.cs](Persistence/Repositories.g.cs) | EF implementation: bounded preparation, scalar creation, shared save, one fresh result query |
| [Generated/TestDoubles.g.cs](Generated/TestDoubles.g.cs) | Optional fixtures: public arrangement rows, ID reservation and fresh related DTOs |
| [Generated/manifest.json](Generated/manifest.json) | Limits, ordered results, shared-save/non-native/non-graph receipts and coverage |

## Declaration to generated API

`HasCommand("CreateEntries").CreateMany<NewEntry, CreateEntryBatch>(...)` yields
`Task<CommandResult<IReadOnlyList<CreatedEntry>>> CreateEntriesAsync(CreateEntryBatch request,
CancellationToken cancellationToken = default)` because it ends in `Save().ReturnsSaved(...)`.
The same mapping ending in `Stage()` yields `Task<CommandResult> StageEntriesAsync(...)`.
There is no synthetic matching key, service-side item mapper or projection DSL.

`HasInMemoryStore` + `MapInMemoryEntity` + `MapInMemoryWrites` provide public fixture rows for
internal models. `HasInMemoryGeneratedValue` supplies a deterministic, reservation-aware test ID,
not a SQL identity simulation. `HasInMemoryNavigation(row => row.Folder)` enables the fresh related
DTO; it does not insert a folder graph.

[Program.cs](Program.cs) demonstrates staging a batch plus another repository's update, shared saves,
input-ordered IDs/DTOs, empty input without flushing, all-or-nothing preparation failure, checkpoint
rollback and baseline restoration.

The consumer references Contracts, Testing and DI only. Persistence output is visible but excluded;
the runner independently compiles it against actual .NET 8 inputs. The executable has no EF, Models,
Designers or generator dependency.

```powershell
pwsh -NoProfile -File samples/ConfigurationExamples/Generate.ps1 -Configuration Release -ValidateCompilation -Run
dotnet run --project samples/ConfigurationExamples/MemoryCreateBatches -c Release --no-build --no-restore
```

Defaults stay simple: no declaration means no create batch; no fixture opt-in means no automatic
simulation. See [creation semantics and limits](../../../docs/implemented/writes/CREATE-MANY-COMMANDS.md). This is
bounded tracked creation, not native bulk insertion, graph persistence or SQL constraint simulation.
