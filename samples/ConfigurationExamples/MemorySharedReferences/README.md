# MemorySharedReferences: additional references with shared columns

Start with [SharedAuditRepositoryDesigner](Configuration/SharedAuditRepositoryDesigner.cs).
The [context](Model/SharedAuditContext.cs) maps a folder/link/resource plus an existing actor.
The actor FK shares PartitionId with the owner FK. The global resource matching key is only
ResourceId, so validating that key cannot establish the new actor reference's partition.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [Contracts](../Contracts/Generated/SharedAuditRepositories.g.cs) | Bound application-owned read/write ports; no EF or persistence entities |
| [Production repositories](Persistence/Repositories.g.cs) | Explicit FK roles and matchesCreatedOwner before ownership binding |
| [Fixture/test doubles](Generated/TestDoubles.g.cs) | The same guard over public rows, with separate collection-simulation permission |
| [Manifest](Generated/manifest.json) | Which additional columns overlap, their roles, and Add-only validation timing |

The designer opts into TargetReference, AdditionalReference and AllowSharedReferenceColumns
explicitly. Add initializes every reference component once; ownership comes from the selected
folder. New shared values must agree before binding. Update changes only caption/version: actor
inputs on a matched resource are ignored, even when they contain another partition or unknown ID.

The [consumer](Program.cs) references Contracts, Testing and DI only. It checks rejected new
cross-partition references, preserved matched actors, fresh DTOs, Stage/shared Save, untouched
referenced entities/other owners, explicit rollback and baseline restore.

This is an optional consistency check, not tenancy, authorization or FK simulation. Database
constraints still establish whether references exist. Optional overlapping references and endpoint
graph persistence remain unsupported. See the [shared-reference guide](../../../docs/implemented/writes/SHARED-ADDITIONAL-REFERENCES.md).

Run: `dotnet run --project MemorySharedReferences.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
