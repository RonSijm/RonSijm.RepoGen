# MemorySharedWrites: designed writes and a shared fixture unit of work

Start with [DraftWorkspaceRepositoryDesigner](Configuration/DraftWorkspaceRepositoryDesigner.cs).
The [EF context](Model/DraftWorkspaceContext.cs) declares caller-supplied primary keys and an ordinary
integer concurrency token. The [consumer](Program.cs) references contracts and Testing, never EF,
Models, Designers or Generation. This is a fixture simulation, not a replacement database provider.

## Designer → generated output

| Declaration | Exact output |
| --- | --- |
| Closed repositories + named Find/Create/Rename/Delete operations | [Read/write Draft and JournalEntry contracts](../Contracts/Generated/DraftWorkspaceRepositories.g.cs) |
| UseScriptedTestDoubles + two HasInMemoryStore declarations | [DraftWorkspaceContextRepositoryFixture and typed stores](Generated/TestDoubles.g.cs), shared by read/write/arrange aliases |
| UseInMemoryTestCommands | Generated data derives from InMemoryWriteDataContext; command handlers reuse the approved production predicates, setters, guards and result mappings |
| StageRename + StageEntry | Two pending entries, no saved-row changes until the shared SaveChangesAsync |
| Rename.Save() + explicit Version setter/ExpectVersion | Applied/Conflict/NotFound outcomes and saved DraftCard; no automatic version/key generation |
| Visible root policy | Hidden drafts cannot be patched/deleted by the ordinary named operations |
| Consumer explicitly registers IRepositoryUnitOfWork to Fixture.Data | One deliberately selected context partition; no automatic replacement of a global application unit of work |
| RequirePortableTestCommands | Unsupported native/graph/proxy/generated-value shapes fail generation unless acknowledged with RequireTestOverride |

[Production output](Persistence/Repositories.g.cs) is checked in and separately compiler-validated.
[Manifest](Generated/manifest.json) lists command coverage and writeSimulation semantics.

The executable checks Stage versus saved reads, cross-repository Save counts, caller keys, explicit
version updates/stale outcomes, root visibility, delete result mappings, rollback of saved and unsaved
changes and independent baseline restore. Transaction commit does **not** imply Save. Pending writes
must be explicitly saved/discarded before beginning a transaction or resetting fixture baselines.

Everything is opt-in. Disabling UseInMemoryTestCommands keeps the existing read/script output and
production API unchanged. Matching scripts override automatic writes; ResetTestDouble resets scripts
and history, not fixture data or pending state.

No SQL constraints, provider races, cache invalidation, generated rowversions, graph reconciliation,
proxy reverse mapping or parallel transaction isolation are promised. Native/provider-only writes
remain explicit override cases. [MemoryBatches](../MemoryBatches/README.md) separately demonstrates
supported bounded root-row batches; [MemoryGeneratedValues](../MemoryGeneratedValues/README.md)
adds explicitly configured fixture scalar factories. See [the write-simulation guide](../../../docs/implemented/testing/INMEMORY-WRITES.md).

Run from this folder: `dotnet run --project MemorySharedWrites.csproj`.
Regenerate/check with the [shared example workflow](../README.md#generate-check-and-run).
