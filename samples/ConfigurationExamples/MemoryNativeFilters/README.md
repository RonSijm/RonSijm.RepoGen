# MemoryNativeFilters: filtered native update/delete results

Start with [NativeNotebookRepositoryDesigner](Configuration/NativeNotebookRepositoryDesigner.cs).
The [tiny context](Model/NativeNotebookContext.cs) declares one scalar soft-delete filter and version
token. The designer independently enables model-filter reuse and immediate simulation per command.
Public immutable seeds/DTOs and handwritten ports keep the consumer EF/model/tooling-free.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [Contracts](../Contracts/Generated/NativeNotebookRepositories.g.cs) | Bound read/write ports and MutationResult, with no runtime EF/filter/projection choices |
| [Production](Persistence/Repositories.g.cs) | Original model-filtered native writer operations and separate tracked StageTitle |
| [Fixtures](Generated/TestDoubles.g.cs) | Saved-row selection, direct changed/deleted values and retained pending state |
| [Manifest](Generated/manifest.json) | Model filter receipt explicitly distinguishes native values from fresh saved queries |

| Declaration | Generated behavior |
| --- | --- |
| Configure + both per-command opt-ins | Portable model filter applied before native selection, independent of entity read flags |
| Update + expected version | Post-update title/version returned; zero matches for filtered, hidden or stale input |
| ArchiveAll + ReturnsMany | Changed Deleted=true rows returned even though fresh Cards hides them |
| Consume + ReturnsMany | Matched pre-delete values returned; no promised result order |
| StageTitle + tracked token | Pending state survives native mutation and may conflict on shared Save |
| All + IgnoreQueryFilters/BypassVisibility | Explicit diagnostic read sees saved rows without changing command filtering |

The [consumer](Program.cs) checks these distinctions, token conflicts, checkpoint rollback and
baseline restore. It calls only named reader/writer methods. This is intentionally different from
[MemorySavedFilters](../MemorySavedFilters/README.md), whose ReturnsSaved does a fresh result read.

Both command options default to off; false removes the corresponding permission. Production
returning capability and scalar/write-map validation remain mandatory. Filtered atomic upsert,
graph/shadow results, context-dependent policies and relational guarantees remain boundaries.
See [full semantics](../../../docs/implemented/testing/INMEMORY-NATIVE-FILTERS.md) and
[immediate simulation](../../../docs/implemented/testing/INMEMORY-IMMEDIATE-WRITES.md).

Run: `dotnet run --project MemoryNativeFilters.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
