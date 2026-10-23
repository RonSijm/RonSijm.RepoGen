# MemoryMappedReturning: mapped scalar mutation receipts

Start with [ReceiptQueueRepositoryDesigner](Configuration/ReceiptQueueRepositoryDesigner.cs).
The [one-table model](Model/ReceiptQueueContext.cs) has a shadow Revision computed from Counter
and a field-backed Category whose public getter adds display formatting. Both production native
results and fixture results return the stored scalar, not that getter's presentation value.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/ReceiptQueueRepositories.g.cs) | Bound reader/writer ports and public request/receipt types |
| [Production operations](Persistence/Repositories.g.cs) | Native update/delete, exact expected versions and getter-free returned buffers |
| [Fixture implementation](Generated/TestDoubles.g.cs) | Typed mapped state and immediate returned-value delegates; no EF/model references |
| [Manifest](Generated/manifest.json) | Independent capture, scalar-state and immediate-simulation permissions |

| Designer declaration | Generated effect |
| --- | --- |
| MapInMemoryProperty for Category and Revision | Exact typed scalar slots, no entity getter or reflection |
| Complete MapInMemoryWrites(..., useMappedPropertyState: true) | Separate permission and structural round-trip checks for fixture state |
| HasInMemoryGeneratedValue("Revision", ...) | Explicit Counter + 1 recipe on updates; never runs on deletes |
| IncludeMappedProperties | Production field/shadow capture; alone grants no fixture simulation |
| SimulateImmediateWrites | Saved-only update/delete with post-update/pre-delete receipts prepared before commit |

The [consumer](Program.cs) checks stale versions, fresh revisions, stored Category values, consume
results, baseline restore and checkpoint rollback without EF, model or designer dependencies.
Services only call named operations; no runtime property-name/mapping DSL is exposed.

The combination is optional. Removing either write-state consent or immediate simulation retains
strict override requirements. A shadow-only model can use IncludeShadowProperties instead of broad
capture. Database buffers, native triggers, constraints, rowversions, locks/races and provider
equivalence are not simulated. Original key/setter/graph restrictions remain.

Run: `dotnet run --project MemoryMappedReturning.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
See [mapped returned-value fixtures](../../../docs/implemented/testing/INMEMORY-MAPPED-RETURNING.md).
