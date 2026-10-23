# MemoryModelFilters: optional model filters, not duplicated visibility

Start with [FilteredNotebookRepositoryDesigner](Configuration/FilteredNotebookRepositoryDesigner.cs).
The [context](Model/FilteredNotebookContext.cs) declares `!Deleted` and `!Archived` on two internal
tables. Each designer root calls `UseInMemoryModelQueryFilter()` instead of repeating those
predicates. The separate `Visible` designer policy checks `!Hidden`.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contract](../Contracts/Generated/FilteredNotebookRepositories.g.cs) | Bound IFilteredNoteReader and public cards/pages/stream |
| [Production repositories](Persistence/Repositories.g.cs) | Normal EF roots; the context supplies its own model filters |
| [Fixture implementation](Generated/TestDoubles.g.cs) | Explicit scalar filter before visibility/pipelines on each detached root |
| [Manifest](Generated/manifest.json) | Per-operation modelQueryFilters, root predicates and independent bypass scope |

| Declaration | Generated effect |
| --- | --- |
| HasQueryFilter in the context | Final-model predicate reused only by explicitly opted-in fixtures |
| UseInMemoryModelQueryFilter on each entity | Row-only filter; off by default, no production/API/cache change |
| Public store and MapInMemoryEntity | Immutable seeds, internal persistence models never constructed by the consumer |
| HasVisibilityPolicy("Visible") | An independent named Hidden policy |
| IgnoreQueryFilters on IncludingDeleted | Bypasses model Deleted filter but keeps Hidden visibility |
| BypassVisibility on IncludingHidden | Bypasses Hidden visibility but keeps model Deleted filter |
| ActiveGroups From join | Applies both final-model filters before joining shared detached snapshots |
| Page/Stream | Filtered totals and cold snapshot-stable DTOs |

The [consumer](Program.cs) checks all of those differences, retained fixture rows and baseline
restoration. Its dependencies are Contracts, Testing/Abstractions and DI, not EF/models/designers/tooling.
Setting the opt-in to false restores the default model-filter override boundary; it never silently
ignores a production filter.

Context-captured tenant values, filtered navigation graphs, AutoInclude, shadow/inherited shapes
and filtered writes are not inferred. See [the precise read-only boundary](../../../docs/implemented/testing/INMEMORY-MODEL-FILTERS.md).

Run: `dotnet run --project MemoryModelFilters.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
