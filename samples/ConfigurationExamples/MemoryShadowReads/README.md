# MemoryShadowReads: model-only values in EF-free fixtures

Start with [RankedNotebookRepositoryDesigner](Configuration/RankedNotebookRepositoryDesigner.cs).
The [context](Model/RankedNotebookContext.cs) maps Rank, Category and Deleted without adding CLR
members to the internal entity. The designer explicitly binds those exact names/types to public
[RankedNoteSeed](Contracts/RankedNoteSeed.cs) data. Consumers never call EF.Property or build queries.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/RankedNotebookRepositories.g.cs) | IRankedNoteReader, public cards, pages, totals and streams |
| [Production operations](Persistence/Repositories.g.cs) | Ordinary EF.Property reads; production translation is unchanged |
| [Fixture implementation](Generated/TestDoubles.g.cs) | Typed plain read-row slots, no EF, runtime property-name lookup or entity construction |
| [Manifest](Generated/manifest.json) | MapInMemoryProperty names/types/value recipes and read-only scope |

| Designer declaration | Generated behavior |
| --- | --- |
| MapInMemoryEntity | Declares ordinary Id/Text/Hidden scalar adaptation without constructing the entity |
| MapInMemoryProperty for each model-only scalar | Exact typed fixture values in predicates, projections, ordering and groups |
| UseInMemoryModelQueryFilter | Separately enables the context's shadow Deleted filter before designer Hidden visibility |
| Cards(minimumRank) | A named read; services do not choose property names or pass expression trees |
| IncludingDeleted | Ignores only the model filter, retaining designer visibility and ordering |
| Page/Totals/Stream | Counted pages, grouped sums and cold snapshot-stable results |

The [EF-free consumer](Program.cs) checks every operation, unchanged fixture rows and baseline
restoration. This is read adaptation, not tracked shadow state: commands, original versions,
generated token advancement, relationship shadow keys and native returned buffers are not inferred.
Missing bindings remain explicit override boundaries, even on an empty fixture.

Run: `dotnet run --project MemoryShadowReads.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
See [the complete scope and safeguards](../../../docs/implemented/testing/INMEMORY-SHADOW-READS.md).
