# MemoryModelWrites: independently opted-in command filters

Start with [FilteredDraftsRepositoryDesigner](Configuration/FilteredDraftsRepositoryDesigner.cs).
The [context](Model/FilteredDraftsContext.cs) declares `!Archived` once on an internal draft table.
An unfiltered internal audit table shares the same fixture/save boundary. Public immutable rows
and handwritten ports keep the consumer independent of EF/models/designers/generation tooling.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [Contracts](../Contracts/Generated/FilteredDraftsRepositories.g.cs) | Bound named read/write/audit ports and explicit CommandResult values |
| [Production repositories](Persistence/Repositories.g.cs) | Existing EF roots; model filters still come from the context |
| [Fixtures](Generated/TestDoubles.g.cs) | Per-command filter before selection and on fresh saved results, never a Save constraint |
| [Manifest](Generated/manifest.json) | PerCommandUseInMemoryModelQueryFilter, NoInsertFilter, SharedSaveDoesNotRecheckQueryFilter and explicit limits |

| Declaration | Generated behavior |
| --- | --- |
| Entity UseInMemoryModelQueryFilter | Read opt-in only; does not authorize automatic filtered writes |
| Command UseInMemoryModelQueryFilter | Independently selected model filter for eligible scalar writes |
| ExpectVersion on Rename/StageText | Existing original-token conflict outcomes |
| StageArchive then StageText | Saved-row selection still matches while pending state accumulates |
| Unfiltered audit Stage | Same shared Save persists two repositories, not only filtered visible rows |
| SimulateImmediateWrites + filtered ExecuteArchive | Saved-row count without flushing/refreshing pending changes |
| Create + ordinary Returns | Query filters do not validate insertion or hide the affected-row DTO |
| ArchiveAndRead + ReturnsSaved | Fresh invisible result fails after save; transaction rollback stays explicit |
| Public scalar read/write maps | Complete immutable fixture rows, no construction of internal persistence entities |

The [consumer](Program.cs) checks those differences, retained seeds, baseline restoration and
checkpoint rollback. Ordinary service methods contain no EF selection DSL.

Removing the command opt-in restores its explicit model-filter override boundary. Context-based
filters, filtered graphs, native returning/upserts and provider guarantees are not inferred. See
[bounded command semantics](../../../docs/implemented/testing/INMEMORY-MODEL-WRITE-FILTERS.md) and the independent
[read-filter example](../MemoryModelFilters/README.md).

Run: `dotnet run --project MemoryModelWrites.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
