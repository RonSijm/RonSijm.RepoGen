# MemoryAdditionalReferences: explicit roles for four FKs

Start with [ReferenceCatalogRepositoryDesigner](Configuration/ReferenceCatalogRepositoryDesigner.cs).
The [context](Model/ReferenceCatalogContext.cs) maps a membership row to its collection, matching item,
composite contributor reference and source reference. The designer names all non-owner roles.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/ReferenceCatalogRepositories.g.cs) | Application-bound named read/write ports |
| [Production repositories](Persistence/Repositories.g.cs) | Explicit scalar Add fields, owner binding and unchanged FK-protected updates |
| [Fixtures](Generated/TestDoubles.g.cs) | The same mappings on public seed rows, with declared related snapshots for saved DTOs |
| [Manifest](Generated/manifest.json) | Matching target plus additional FK/principal-key roles and their create-only/provider boundaries |

| Designer declaration | Generated effect |
| --- | --- |
| ReconcileLinks + TargetReference(Item) | ItemId is the complete matching identity, not an inferred contributor/source key |
| AdditionalReference(Contributor) | Add must supply both Region and Code for the alternate principal key |
| AdditionalReference(Source) | Add must supply SourceId; matching updates cannot rewrite it |
| Caption/version Update | Matched inputs retain their original contributor/source despite different Add-only input values |
| Merge / Replace / Stage | Deliberate missing-item policy and shared pending/saved-state behavior |
| Declared contributor/source navigation + ReturnsSaved | Fresh DTOs read existing referenced records after saving |
| Public scalar seed/read/write maps | Internal EF models are neither exposed nor instantiated by the consumer |

The [consumer](Program.cs) replaces links, verifies immutable references on a matched row and explicit
references on a new row, preserves another owner, stages/saves, rolls back and restores a baseline.
It references only Contracts, Testing/Abstractions and DI: no EF, Models, Designers or generator.

There is no global multi-reference switch. Removing a role from this four-FK model fails generation;
ordinary unconfigured two-FK models remain unchanged. The fixture does not validate SQL FK existence,
create contributors/sources, authorize references or implement a workflow.

See [named-role semantics and conservative boundaries](../../../docs/implemented/writes/ADDITIONAL-LINK-REFERENCES.md).
Run: `dotnet run --project MemoryAdditionalReferences.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
