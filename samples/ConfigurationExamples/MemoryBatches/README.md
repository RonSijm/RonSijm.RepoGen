# MemoryBatches: designed bounded batches without EF in the consumer

Start with [BatchWorkspaceRepositoryDesigner](Configuration/BatchWorkspaceRepositoryDesigner.cs).
It declares the same named patch/delete operations for the production repository and its optional
test double. The [model](Model/BatchWorkspaceContext.cs) uses an ordinary generated-on-insert primary
key, a unique `Code` index and an explicit integer concurrency token. The fixture seeds known existing
keys; it does **not** generate keys. The [consumer](Program.cs) references contracts and Testing only.

## Designer → generated output

| Declaration | Exact output |
| --- | --- |
| Closed repositories + `ListOwned`, `UpdateTitles`, `StageTitles`, `DeleteDocuments` | [IReadBatchDocumentRepository / IWriteBatchDocumentRepository](../Contracts/Generated/BatchWorkspaceRepositories.g.cs) |
| `UseScriptedTestDoubles` + `HasInMemoryStore(row => row.Id, copy)` | [BatchWorkspaceContextRepositoryFixture, typed store and script hooks](Generated/TestDoubles.g.cs) |
| `UseInMemoryTestCommands` + `PatchMany` / `DeleteMany` | Automatic generated batch handlers; scripts still take precedence |
| `row => row.Code` / `item => item.Code`, `maximumItems: 4` | Unique scalar matching independently of the fixture primary key, bounded/duplicate-checked inputs |
| Root owner predicate + `Current` visibility policy | Every supplied target must exist and be visible to that operation; one missing target rejects the whole batch |
| Conditional `Optional<string?>` title + explicit version setter / expected token | Omitted and supplied-null values remain different; a stale last item cannot partially stage earlier items |
| `.Stage()` versus `.Save().Returns(...)` | Pending-only changes versus one shared Save with detached DTOs in **input order** |
| `RequirePortableTestCommands` | Unsupported shapes fail at generation unless explicitly acknowledged as requiring a script |

[Production output](Persistence/Repositories.g.cs) is checked in and independently compiler-validated,
but excluded from this executable. [Manifest](Generated/manifest.json) records the automatic write
coverage and pending/saved/batch semantics. The EF input and designer assemblies are not runtime
consumer dependencies.

The executable checks missing/other-owner targets, stale versions, atomic preparation, supplied null
versus omitted fields, alternate-key matching, Stage/shared Save, saved result order, bounded duplicate
inputs, checkpoint rollback and independent baseline restore. Reads inspect saved state, not pending
edits. Batch target rows with existing modifications must be saved or discarded first; unrelated
pending rows remain part of the shared unit of work.

[MemorySharedWrites](../MemorySharedWrites/README.md) focuses on individual commands and
cross-repository saves; this example focuses on bounded root-row batches. Neither simulates SQL
constraints, SQL collation, provider races, cache observers, generated rowversions or graph/cascade
deletes. [MemoryGeneratedValues](../MemoryGeneratedValues/README.md) adds explicit fixture scalar
factories for suitable generated-key creates and computed-on-update values. Native writes and proxy
reverse mapping still require overrides or a real-provider fixture. Everything remains off by default.

Run from this folder: `dotnet run --project MemoryBatches.csproj`.
Regenerate/check with the [shared example workflow](../README.md#generate-check-and-run).
See [fixture batch semantics](../../../docs/implemented/testing/INMEMORY-BATCHES.md) and [production batch semantics](../../../docs/implemented/writes/BATCH-COMMANDS.md).
