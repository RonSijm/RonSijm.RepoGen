# MemoryFilteredCollections: separate read, write and saved-result filters

Start with [FilteredChecklistRepositoryDesigner](Configuration/FilteredChecklistRepositoryDesigner.cs).
The [model](Model/FilteredChecklistContext.cs) declares archive filters on a checklist and its
ordinary child table. The designer supplies public seed/write maps, bounded reconciliation and
three independent permissions: filtered reads, filtered collection mutation, and fresh saved reads.
Services call named operations; they do not construct EF queries or setter expressions.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [Contracts](../Contracts/Generated/FilteredChecklistRepositories.g.cs) | Cards, StageItems, ReplaceItems and StageText operations |
| [Production repositories](Persistence/Repositories.g.cs) | Existing tracked reconciliation and model-filtered child query |
| [Fixture/test doubles](Generated/TestDoubles.g.cs) | Public stores and model-filter predicates passed into owner-scoped reconciliation |
| [Manifest](Generated/manifest.json) | Independent selection/insert/save/result boundaries and accepted child filters |

| Designer choice | Effect |
| --- | --- |
| Entity.UseInMemoryModelQueryFilter(includeNavigations: true) | Read/saved-graph permission; does not enable collection mutations |
| Command.UseInMemoryModelQueryFilter | Parent selection follows its final-model filter |
| SimulateCollectionWrites(includeModelQueryFilters: true) | Explicitly permits child model filters before bounds, matching and missing-item policies |
| ReplaceItems' includeNavigations argument | Independently permits its fresh saved filtered DTO |
| Child Visible policy | Limits mutation candidates; it is not implicitly injected into a DTO collection |
| Complete public write maps and an ID factory | Persistence classes stay internal; IDs account for filtered saved rows too |

The [consumer](Program.cs) references Contracts, Testing and DI only. It verifies that Stage leaves
saved reads unchanged, one shared Save persists the planned changes, archived/hidden children are
not missing-item deletion candidates, and new/updated rows may become archived. The separate
saved query filters its DTO, not the insertion/save. Dirty hidden-child changes still block
reconciliation, and an explicit transaction restores the saved fixture state.

This adds no defaults or inferred soft-delete behavior. The designer explicitly maps Archived
from the input; Delete missing-items remains a tracked delete, not an archive operation.
SQL unique indexes/FKs and races remain provider guarantees: a new logical key matching a filtered
row can fail a database constraint that fixtures do not simulate. See the
[full guide](../../../docs/implemented/testing/INMEMORY-COLLECTION-FILTERS.md).

Run: `dotnet run --project MemoryFilteredCollections.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
