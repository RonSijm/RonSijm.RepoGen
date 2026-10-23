# MappedCollectionSetters: designed parent/child writes

Start with [ReviewBoardsRepositoryDesigner](Configuration/ReviewBoardsRepositoryDesigner.cs).
Its [two-table context](Model/ReviewBoardsContext.cs) maps separate parent/child shadow revisions,
a field-backed item Caption, a field-only edit counter and a nullable indexer remark. Services
receive public DTOs and named methods, not EF entities, model names or setter builders.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/ReviewBoardsRepositories.g.cs) | Handwritten reader/writer ports exposed by generated CQRS interfaces |
| [Production implementation](Persistence/Repositories.g.cs) | Parent Entry state, owner-scoped bounded child queries and Entry(child) assignments |
| [Fixture implementation](Generated/TestDoubles.g.cs) | Independent complete parent/child scalar slots and coordinated pending/saved state |
| [Manifest](Generated/manifest.json) | Exact mapped targets, scoped permissions, bounds and independent fixture capability |

| Designer declaration | Generated effect |
| --- | --- |
| Parent named revision setter + root `AllowWrite<int>` | Explicit parent token advancement, separate from original-version checking |
| `MaximumItems(10)` | Bounds supplied items and existing visible children; excess fails, not truncates |
| Child `SetPropertyWhen<string>("Caption", ...)` | Conditional mapped field assignment; configured field access avoids the display getter |
| Child `SetProperty<int>("edits", ...)` | Reads and increments this child's field-only value through EF entry state |
| Child `AllowWrite<int>("revision")` | A separate grant; root permission never authorizes child writes |
| `ProtectWrite<string?>("review remark")` + child grant | Exact typed design-time write protection, not runtime authorization |
| Parent and child `ExpectVersion<int>("revision", ...)` | Checks original caller tokens at each scope, without inferred advancement |
| Merge / Replace | Keep omitted rows / explicitly delete omitted visible owned rows |
| `When(request => request.IncludeItems)` | Skips the collection and its input evaluation, not independent parent assignments |
| `Stage()` / `Save().ReturnsSaved(...)` | Pending-only changes / shared Save followed by a fresh public DTO graph |
| Complete mapped write state on both stores + `SimulateCollectionWrites()` | Independent fixture capability; parent/read permission is insufficient |

The [EF/model-free program](Program.cs) checks stale-child restoration, conditional Caption omission,
separate token advancement, nullable remarks, Keep/Delete, Stage/shared Save, checkpoints and
baselines. Omitting Caption still advances the explicitly configured edit counter/token and
assigns the remark; that is the designer recipe, not a persistence convention.

There is no Add recipe: unknown keys are rejected. Named updates affect matched rows, not new-row
initializers. Keys, ownership/FKs, unique identity, generated columns, endpoint graphs, skip
navigations and native/atomic targets retain their guards. Fixtures do not promise SQL constraints,
isolation, generated rowversions or race equivalence. Defaults and prior output are unchanged.

Run: `dotnet run --project MappedCollectionSetters.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
See [mapped reconciliation setters](../../../docs/implemented/writes/MAPPED-RECONCILIATION-SETTERS.md).
