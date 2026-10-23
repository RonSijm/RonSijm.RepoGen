# MemoryMappedCollections: shadow versions in bounded collections

Start with [RevisionChecklistRepositoryDesigner](Configuration/RevisionChecklistRepositoryDesigner.cs).
The [two-table model](Model/VersionedChecklistContext.cs) has separate parent/child shadow Revision
tokens computed from ordinary Counter columns. The designer explicitly maps both stores and
supplies their fixture recipes. Keys, matching Code and ownership remain ordinary scalars.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/VersionedChecklistRepositories.g.cs) | Named writer/item-reader ports with public DTOs only |
| [Production operations](Persistence/Repositories.g.cs) | Bounded reconciliation, original EF token checks and fresh saved scalar query |
| [Fixture implementation](Generated/TestDoubles.g.cs) | Independent parent/child typed slots, mapped original versions and shared Save |
| [Manifest](Generated/manifest.json) | Per-participant mappedScalarState, existing collection scope and no provider equivalence |

| Designer declaration | Generated effect |
| --- | --- |
| MapInMemoryProperty("Revision", ...) on each entity | Exact typed shadow values; identically named tokens remain separate |
| Complete MapInMemoryWrites(..., useMappedPropertyState: true) on each entity | Independent write consent and structural round-trip validation |
| HasInMemoryGeneratedValue("Revision", ...) | Explicit Counter + 1 recipes, not inferred database behavior |
| ReconcileCollection + SimulateCollectionWrites | Existing owner-scoped bounded add/update/delete rules over the declared stores |
| ExpectVersion on parent and child | Original input checks, with pending originals rechecked on shared Save |
| Stage versus Save / ReturnsSaved | Deferred publication versus shared persistence and a fresh parent scalar |

The [consumer](Program.cs) checks stale rejection, Stage/shared Save, new child revisions, missing-item
deletion, baseline restore and checkpoint rollback without EF, models or designer dependencies.
Services do not mutate a tracked entity graph or choose property names at runtime.

All choices are optional. Scalar mappings alone do not enable collections; each mapped participant
and command grants its own permission. Existing ownership/key, model-filter, visibility and graph
guards remain. No SQL constraints, generated identity, rowversion or race guarantees are simulated.

Run: `dotnet run --project MemoryMappedCollections.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
See [mapped fixture collections](../../../docs/implemented/testing/INMEMORY-MAPPED-COLLECTIONS.md).
