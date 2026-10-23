# MemoryMappedKeys: explicit keys for detached fixture relationships

Start with [KeyedNotebookRepositoryDesigner](Configuration/KeyedNotebookRepositoryDesigner.cs).
The [two-table model](Model/KeyedNotebookContext.cs) uses a field-backed Folder.Code alternate key
and a shadow Memo.FolderCode foreign key. The designer binds those scalars from public fixture
data, then explicitly permits each navigation to use those values for relationship matching.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/KeyedNotebookRepositories.g.cs) | Two bound read ports; only public DTOs and named CardsAsync operations |
| [Production operations](Persistence/Repositories.g.cs) | Original EF navigation projections; no fixture policy or service-side DSL |
| [Fixture implementation](Generated/TestDoubles.g.cs) | Collection/reference loading through typed key slots from one detached snapshot |
| [Manifest](Generated/manifest.json) | Per-edge mappedPropertyKeys receipts with exact source/target property types and read-only scope |

| Designer declaration | Generated effect |
| --- | --- |
| MapInMemoryProperty for Folder.Code | Stored column value, not the display-oriented entity getter |
| MapInMemoryProperty for Memo.FolderCode | Typed shadow FK from the public Memo seed |
| HasInMemoryNavigation(..., useMappedPropertyKeys: true) | Separate permission to use those values as keys for that edge |
| Same opt-in on the inverse | Independent reference loading; one edge does not grant permission to another |
| HasInMemoryStore keyed by Id | Dictionary identity remains separate from the model's alternate relationship key |

Removing the per-edge permission makes this relationship require a typed override in strict mode;
scalar read bindings alone do not grant it. No entity constructor, field getter, indexer or runtime
reflection is used by the fixture. Writes, original versions, implicit joins, filter permissions,
comparison and provider guarantees retain their separate boundaries. Everything remains opt-in.

The [consumer](Program.cs) verifies both directions, null keys, fresh snapshots, DTO isolation and
baseline restoration. It references Contracts, Testing/Abstractions and DI, not EF or Models.

Run: `dotnet run --project MemoryMappedKeys.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
See [mapped relationship keys](../../../docs/implemented/testing/INMEMORY-MAPPED-KEYS.md) and
[the preceding scalar-only example](../MemoryMappedReads/README.md).
