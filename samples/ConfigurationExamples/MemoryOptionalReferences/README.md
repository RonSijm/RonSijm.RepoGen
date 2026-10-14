# MemoryOptionalReferences: nullable create-only FK roles

Start with [OptionalAssignmentsRepositoryDesigner](Configuration/OptionalAssignmentsRepositoryDesigner.cs).
The [context](Model/OptionalAssignmentsContext.cs) has one required owner, one required matching
subject and an optional composite origin reference. The latter uses nullable int/string FK columns.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/OptionalAssignmentsRepositories.g.cs) | Handwritten read/write application ports and named request/result methods |
| [Production repositories](Persistence/Repositories.g.cs) | Once-only Add initializer and generated all-or-none composite null guard |
| [Fixtures](Generated/TestDoubles.g.cs) | The same guard on private scalar proxies backed by public seed records |
| [Manifest](Generated/manifest.json) | Explicit optional role, null semantics, immutable matches and creation-only validation timing |

| Designer declaration | Generated effect |
| --- | --- |
| TargetReference(Subject) | Complete required SubjectId defines matching identity |
| OptionalAdditionalReference(Origin) | Nullable Partition/Code must both be absent or both be present on a new row |
| Complete Add initializer | Both nullable FK fields are explicit; the initializer runs once per new row |
| Caption/version Update | Matched origins keep their values and presence, regardless of unused creation inputs |
| Replace / Stage | Deliberate missing-item policy, shared pending/saved-state behavior and rollback |
| ReturnsSaved | Fresh nullable scalar result DTOs after the save; no implicit origin navigation read |
| Public scalar seed/read/write maps | No EF model construction/exposure in the consumer |

The [consumer](Program.cs) creates an absent reference, preserves present/absent matched references,
rejects a partial composite key without changing saved/pending state, stages a present reference,
saves, rolls back and restores a baseline. Another owner and referenced rows remain untouched.
Only Contracts, Testing/Abstractions and DI are referenced by the consumer.

There is no mandatory/global optional-reference setting. Required references still use
AdditionalReference; matching targets cannot be optional. Fixtures do not enforce SQL FK existence
or authorize references. This example covers writes, unlike the [nullable-read example](../MemoryOptionalLinks/README.md).

See [configuration, semantics and limits](../../../docs/implemented/writes/OPTIONAL-ADDITIONAL-REFERENCES.md).
Run: `dotnet run --project MemoryOptionalReferences.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
