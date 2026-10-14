# MemorySharedLinks: shared owner/reference columns

Start with [ScopedResourcesRepositoryDesigner](Configuration/ScopedResourcesRepositoryDesigner.cs).
The [context](Model/ScopedResourcesContext.cs) explicitly models Folder/Resource keys and their join.
PartitionId belongs to both FKs. The designer opts into that overlap, names the service operations,
and separately opts into their fixture implementation.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/ScopedResourcesRepositories.g.cs) | Named read/write interfaces bound to the handwritten application ports |
| [Production repositories](Persistence/Repositories.g.cs) | Typed owner consistency checks, complete target matching and tracked shared save |
| [Fixtures](Generated/TestDoubles.g.cs) | The same guards on public mapped seeds, with no persistence-model instantiation |
| [Manifest](Generated/manifest.json) | Shared column roles/positions, validation timing, no normalization and explicit fixture coverage |

| Designer declaration | Generated effect |
| --- | --- |
| ReconcileLinks with (PartitionId, ResourceId) | Complete immutable target-reference matching |
| AllowSharedReferenceColumns() | Every supplied partition must equal the selected folder's partition |
| Add with PartitionId / ResourceId | Creates only a scalar join row; FolderId is bound from the selected owner |
| Payload/version Update | Changes only approved non-FK scalars; neither resource nor ownership is transferred |
| MergeResources / ReplaceResources | Explicit Keep versus Delete-missing policies |
| StageResources / shared Save | Pending join changes do not become saved reads until the shared save |
| HasInMemoryNavigation / ReturnsSaved | Fresh DTOs read existing resources through explicitly declared relationships |
| Public seed/read/write maps | The consumer never references or constructs internal EF models |

The [consumer](Program.cs) first rejects a cross-partition reference with no residual pending changes.
It then merges, stages, saves, checks another partition/target table, rolls back and restores a baseline.
It references only Contracts, Testing/Abstractions and DI; no EF, Models, Designers or generator.

The option is off by default. Removing it from this overlapping model produces RGD3902; enabling it
on disjoint links adds nothing. It is not tenancy resolution or authorization. SQL FK constraints
and provider concurrency/races remain real-provider tests rather than automatic fixture behavior.

See [shared-reference semantics, configuration and limits](../../../docs/implemented/writes/SHARED-REFERENCE-COLUMNS.md).
Run: `dotnet run --project MemorySharedLinks.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
