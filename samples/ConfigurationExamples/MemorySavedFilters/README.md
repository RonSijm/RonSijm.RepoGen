# MemorySavedFilters: fresh filtered DTOs after scalar writes

Start with [SavedShelfRepositoryDesigner](Configuration/SavedShelfRepositoryDesigner.cs).
The [two-table context](Model/SavedShelfContext.cs) defines archive/soft-delete filters once.
The designer configures scalar commands, public read/write maps and separate filtered saved-result
permission. The executable references only contracts/testing/DI; it constructs no persistence entities.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [Contracts](../Contracts/Generated/SavedShelfRepositories.g.cs) | Bound entry reader/writer and shelf writer; no EF/filter/projection choices at runtime |
| [Production](Persistence/Repositories.g.cs) | Original scalar writes and fresh model-filtered result query after the shared Save |
| [Fixtures](Generated/TestDoubles.g.cs) | Public maps, post-save snapshot, filters before relationship assignment and ordered results |
| [Manifest](Generated/manifest.json) | savedModelQueryFilters permissions/order plus unchanged write and committed-result semantics |

| Declaration | Generated behavior |
| --- | --- |
| ConfigureResult + per-command includeNavigations | Same filtered saved recipe for scalar patch/create and bounded batches |
| Entry read filter disabled | Command still owns its root filter; read permission is not borrowed |
| Shelf entity graph permission | Separate model archive filter on the related dataset |
| Explicit Shelf/Entries relationships and null guards | Filtered optional targets become null; collection counts omit deleted rows |
| Visible root policy | Hidden roots cannot be mutated by these commands; unrelated child-root visibility is not injected |
| Shelf StageArchive + entry Rename | One shared Save, then the fresh result sees the related archive |
| Archive + ReturnsSaved | Saved root becomes hidden, so result failure occurs after persistence |
| All + IgnoreQueryFilters/BypassVisibility | Explicit diagnostic read of saved rows; no production command filter-bypass API |

The [consumer](Program.cs) checks filtered counts/optional targets, input-ordered creation/patch
batches, separate selection rules, shared Save, post-save failure, explicit rollback and baseline
restore. It calls named ports only. The result-count example demonstrates a read graph, not a child
mutation/reconciliation feature.

The new command argument defaults to false. Parameterless removes graph permission; false clears
the entire command filter setting. Required filtered targets, filtered many-to-many, unsafe access,
context-dependent filters and graph mutations remain scripted/provider boundaries.
See [full semantics](../../../docs/implemented/testing/INMEMORY-SAVED-FILTERS.md) and
[scalar command filters](../../../docs/implemented/testing/INMEMORY-MODEL-WRITE-FILTERS.md).

Run: `dotnet run --project MemorySavedFilters.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
