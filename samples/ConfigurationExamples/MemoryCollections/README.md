# MemoryCollections: designed child writes, separate test tables

Start with [ChecklistWorkspaceRepositoryDesigner](Configuration/ChecklistWorkspaceRepositoryDesigner.cs).
It configures bounded child reconciliation once for production, then explicitly opts those named
commands into in-memory execution. [Program.cs](Program.cs) only arranges public rows and calls
generated methods. It never runs the designer, constructs EF entities or assembles micro-extensions.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs this designer with
[ChecklistWorkspaceContext](Model/ChecklistWorkspaceContext.cs), emitting these four artifacts:

| Output | What to inspect |
| --- | --- |
| [CQRS facade](../Contracts/Generated/ChecklistWorkspaceRepositories.g.cs) | `IWriteStoredChecklistRepository.StageItemsAsync` / `SaveItemsAsync`; `IReadStoredChecklistItemRepository.ListItemsAsync`; child `StageTextAsync` |
| [Test doubles and shared fixture](Generated/TestDoubles.g.cs) | `StoredChecklistRepositoryTestDouble`, typed scripts, generated `ReconcileAsync` handlers, `ChecklistWorkspaceContextRepositoryFixture` and public keyed stores |
| [Real persistence](Persistence/Repositories.g.cs) | The same named methods using `TrackedCollectionReconciler` and EF; excluded from this consumer executable |
| [Manifest](Generated/manifest.json) | Generated operation coverage and `collectionSimulation`, including scope, bounds, policies and excluded guarantees |

| Designer declaration | Specific generated consequence |
| --- | --- |
| `HasCommand("StageItems")...Stage()` | `StageItemsAsync(ChecklistChange, CancellationToken)` returns `CommandResult`; no save or projected result |
| `HasCommand("SaveItems")...Save().Returns(...)` | `SaveItemsAsync(...)` returns `CommandResult<ChecklistReceipt>` after the shared save |
| `Where` with ID + owner and parent `ExpectVersion` | Both backends reject missing ownership/stale parent state before child mutations |
| `ReconcileCollection(...).MaximumItems(5)` | Bounded input plus existing visible child count; match by `Code`, not generated primary ID |
| `Add` + child `HasInMemoryGeneratedValue` | Explicit scalar child creation, reserved row-derived ID and ownership FKs bound from the selected parent |
| `Update` + child version setter/check | Approved scalar updates and expected-version conflict, not a reverse DTO mapper |
| `MissingItems(Delete)` + child `HasVisibilityPolicy` | Remove absent **visible** rows only; hidden and other-owner rows stay out of scope |
| `SimulateCollectionWrites()` | Generated fixture execution of those same rules; off by default, does not change production output |
| Public seed/read/write adapters on both entities | Internal model types become plain scalar testing slots; public immutable fixture rows preserve arrangement-only `FixtureNote` |
| Child `StageText` | Another owner can stage an independent change that participates in the shared save |

## Consumer sequence

1. Arrange parent and child tables separately through their public stores; capture a baseline.
2. Stage an independent child write and the parent reconciliation; queries still return saved rows.
3. Save once: update the parent + existing child, insert a new child, delete the absent visible child
   and save the unrelated staging. The fixture reports five affected rows.
4. Verify that hidden membership and arrangement-only metadata survive, and the input did not supply
   the new child's owner or ID.
5. Try a stale child version: return `Conflict` without leaving a partially advanced parent version.
6. Save empty input inside a fixture checkpoint transaction, then verify rollback restores both tables.

The executable depends only on the EF-free Contracts/Testing assemblies and DI. The internal
[parent](Model/StoredChecklist.cs) and [child](Model/StoredChecklistItem.cs) types are generation
inputs compiled elsewhere; they are not runtime consumer dependencies.

```powershell
dotnet run --project samples/ConfigurationExamples/MemoryCollections -c Release
```

Regenerate and check the actual .NET 8 contracts/testing output with the shared
[Generate.ps1](../Generate.ps1) script. See [fixture reconciliation semantics](../../../docs/implemented/testing/INMEMORY-COLLECTIONS.md)
for skipped/null/empty input, Keep/Reject policies, multiple collections, composite ownership,
shared-save failure and unsupported graph results. Shared generated fake/SQLite tests cover the
portable subset, not SQL constraints, identities, locks, cascades or universal database equivalence.
