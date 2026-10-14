# MemoryFilteredNavigation: related soft-delete filters

Start with [FilteredShelfRepositoryDesigner](Configuration/FilteredShelfRepositoryDesigner.cs).
The [context](Model/FilteredShelfContext.cs) defines archive and soft-delete filters on two internal
tables. The designer separately permits their reuse in fixture graphs. Public immutable seeds,
DTOs and handwritten reader ports keep the consumer free of EF/models/designers/tooling.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [Contracts](../Contracts/Generated/FilteredShelfRepositories.g.cs) | Bound named shelf/entry readers; no runtime filter or EF choices |
| [Production](Persistence/Repositories.g.cs) | Original model-filtered queries and explicit Cards Include; IgnoreQueryFilters on All |
| [Fixtures](Generated/TestDoubles.g.cs) | Public maps, filters before relationships, optional nulls and one detached graph snapshot |
| [Manifest](Generated/manifest.json) | Graph opt-ins/predicates, pre-loading order, bypass and honest unsupported-shape boundaries |

| Declaration | Generated behavior |
| --- | --- |
| Both entities: UseInMemoryModelQueryFilter(includeNavigations: true) | Final-model !Archived / !Deleted reused, not copied into designer expressions |
| Explicit Entries and Shelf navigations | Filtered collection children; filtered optional shelf becomes null |
| Shelf Cards projection | Count/titles exclude deleted entries, but retain entry-root Hidden rows |
| Entry Visible policy | Hidden entries omitted from entry roots, not silently from shelf collections |
| Entry null-guarded named projection | Missing/filtered shelf has null VisibleShelfId |
| All + IgnoreQueryFilters | Restores model-filtered rows/relationships while retaining root visibility |
| From / Page / Stream | Same pre-loading policy across typed roots, filtered counts and cold snapshots |

The [consumer](Program.cs) checks all these behaviors, caller mutation isolation, seed retention,
stream snapshots and baseline restore. It calls only named reader methods.

Removing includeNavigations restores the earlier explicit graph-override boundary; ordinary row
filter permission remains. Required filtered references, filtered many-to-many, unsafe optional
dereferences, context-dependent policies and graph writes are deliberately not simulated.
See [full semantics and boundaries](../../../docs/implemented/testing/INMEMORY-FILTERED-NAVIGATION.md).

Run: `dotnet run --project MemoryFilteredNavigation.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
