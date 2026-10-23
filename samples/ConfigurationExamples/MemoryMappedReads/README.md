# MemoryMappedReads: stored scalars without entity behavior

Start with [MappedNotebookRepositoryDesigner](Configuration/MappedNotebookRepositoryDesigner.cs).
The [internal entity](Model/StoredMappedMemo.cs) has a display-oriented Caption getter, private
revision/deletion fields and an object indexer. Its [EF model](Model/MappedNotebookContext.cs)
maps their stored columns. The designer explicitly supplies those values from public
[MappedMemoSeed](Contracts/MappedMemoSeed.cs) data; fixtures never construct or invoke that entity.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free read contract](../Contracts/Generated/MappedNotebookRepositories.g.cs) | IMappedMemoReader with named reads and public cards/pages |
| [Production operations](Persistence/Repositories.g.cs) | Normal EF column/member/indexer selection and model filtering |
| [Fixture implementation](Generated/TestDoubles.g.cs) | Typed scalar slots instead of entity constructors, getters, reflection or indexer calls |
| [Manifest](Generated/manifest.json) | Explicit propertyMappings, access kinds and per-query propertyReads |

| Designer declaration | Generated effect |
| --- | --- |
| MapInMemoryEntity maps Id | Ordinary key declaration; no persistence entity factory |
| MapInMemoryProperty for Caption | Stored field-backed column, not the display getter's prefix |
| MapInMemoryProperty for revision/deleted | Exact named field-only values, without public CLR properties |
| MapInMemoryProperty for Source | Exact model indexer value, with a constant-key typed cast in the query |
| UseInMemoryModelQueryFilter | Separately enables the field-only deletion filter |
| AllowSorting on Caption | Whitelisted ordering through the bound slot, not runtime reflection |
| Recent/Page | Named field-based filtering and counted pages with no EF methods in services |

The [consumer](Program.cs) verifies those behaviors and snapshot/baseline isolation. It depends on
Contracts, Testing/Abstractions and DI, not EF, Models, Designers or generation tooling.
An initializer and a named binding cannot both assign the same model scalar. Unbound/dynamic
indexers and opaque entity behavior retain diagnostics rather than executing domain code.
This is read adaptation, not tracked field/indexer/token state or relationship-key simulation.

Run: `dotnet run --project MemoryMappedReads.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
See [the unified property-read API](../../../docs/implemented/testing/INMEMORY-MAPPED-READS.md) and
the separate [shadow-only example](../MemoryShadowReads/README.md).
