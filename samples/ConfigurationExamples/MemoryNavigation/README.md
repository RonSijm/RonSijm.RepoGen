# MemoryNavigation: declared relationships and nested DTO reads

Start with [NavigationCatalogRepositoryDesigner](Configuration/NavigationCatalogRepositoryDesigner.cs).
The [NavigationCatalogContext](Model/NavigationCatalogContext.cs) declares the actual EF relationship:
CatalogEntry.GroupCode + GroupRegion → CatalogGroup.Code + Region, plus an optional Category reference.
The designer opts individual navigations into testing, not a second set of relationship keys.

Public [fixture row shapes](Contracts/CatalogGroup.cs) live in Contracts so the [consumer](Program.cs)
can arrange them without referencing EF, Models, Designers or Generation. Application
[ICatalogBrowse](Contracts/ICatalogBrowse.cs) returns DTOs, never those persistence entities.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) explicitly pairs this designer and context.

| Declaration | Exact output |
| --- | --- |
| UseClosedRepositories + BindReadPort<ICatalogBrowse> | [IReadCatalogGroupRepository : ICatalogBrowse](../Contracts/Generated/NavigationCatalogRepositories.g.cs), with six named operations |
| Three HasInMemoryStore declarations | [NavigationCatalogContextRepositoryData](Generated/TestDoubles.g.cs), with typed Group/Entry/Category stores and one shared fixture |
| Group.HasInMemoryNavigation(Entries) | Browse, Detail, Page, RawCounts and Stream load entries by the EF composite alternate-key relationship |
| Entry.HasInMemoryNavigation(Category) | Nested CatalogEntryCard.Category reads the actual category store, with optional missing references |
| Entry.HasInMemoryNavigation(Group) + From entry root | ItemsAsync names its group by the same keys; null components never join |
| Named projection + explicit child Where(!Hidden) | Group cards exclude hidden entries, while RawCountsAsync deliberately counts all related entries |
| RequireNavigationVisibilityReview + review reasons | Existing production navigation-policy checks/receipts remain active; no fake-only filter injection |
| Unfiltered Full load profile with ThenInclude | DetailAsync uses the same designed loading paths; no silent filtered-Include approximation |
| Paged and Stream terminals | One coordinated detached graph per page/stream enumeration, with cold first-move capture |

[Production output](Persistence/Repositories.g.cs) is checked in and independently compiler-validated.
It is excluded from this EF-free executable. [Manifest](Generated/manifest.json) includes per-operation
navigationSnapshot receipts listing exact stores, edge names and direction-specific keys.

## What this example checks

The executable verifies shared DI fixture identity, composite-key separation between two regions,
nested DTOs, optional references/empty collections, explicit child visibility, a named unfiltered
load profile, counted paging, From root policy versus related policy, cold streams and coordinated
baseline restore. All seeds are scalar rows; none needs a pre-linked navigation graph. Loading
never changes the arranged Group.Entries collection.

Testing settings and relationship declarations are optional. Remove a HasInMemoryNavigation
declaration to require a typed override for dependent reads, or disable UseInMemoryTestQueries
to keep script-only fixtures. Production queries/contracts/API/cache fingerprints stay unchanged.
No tracking, entity/business behavior adaptation, fake writes or SQL constraints are promised.

Unit tests also cover nested reference/collection chains, required-reference errors, duplicate
alternate keys, self/inverse identity inside a snapshot, keyed partitions, cancellation, scripts
and real generated SQLite contract comparisons. Custom converted/compared keys, shadow keys,
filtered loading and skip/many-to-many navigations remain explicit override boundaries.
[MemoryProxyNavigation](../MemoryProxyNavigation/README.md) separately demonstrates public scalar
maps and plain navigation slots for internal/read-only/private-setter models, without simulating
their entity/custom collection behavior. See [the complete navigation guide](../../../docs/implemented/testing/INMEMORY-NAVIGATION.md).

Run from this folder: `dotnet run --project MemoryNavigation.csproj`.
Regenerate/check using the [shared example workflow](../README.md#generate-check-and-run).
