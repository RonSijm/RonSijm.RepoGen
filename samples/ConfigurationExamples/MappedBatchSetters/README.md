# MappedBatchSetters: one designed batch, typed service methods

Start with [ReviewEntriesRepositoryDesigner](Configuration/ReviewEntriesRepositoryDesigner.cs).
Its [one-table context](Model/ReviewEntriesContext.cs) maps a field-backed Caption, a field-only
edit counter, a shadow revision token and a nullable indexer remark. The designer owns the
assignments, original-version check, twenty-item limit and explicit Save/Stage choice.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/ReviewEntriesRepositories.g.cs) | Named request-to-result operations implementing the handwritten read/write ports |
| [Production implementation](Persistence/Repositories.g.cs) | One bounded target query; typed Entry.CurrentValue assignments; existing shared Save/Stage and snapshots |
| [Fixture implementation](Generated/TestDoubles.g.cs) | Independently permitted complete scalar state; no EF/model/generator reference in the consumer |
| [Manifest](Generated/manifest.json) | Exact mapped batch targets, type/shadow bindings, bounds and fixture capability |

| Designer declaration | Generated effect |
| --- | --- |
| `PatchMany(..., maximumItems: 20)` | Validates and snapshots input once; missing/hidden targets cannot partially stage a batch |
| `SetPropertyWhen<string>(nameof(ReviewEntry.Caption), ...)` | Conditional field assignment; configured field access avoids the behavioral display getter |
| `SetProperty<int>("edits", row => EF.Property<int>(row, "edits") + 1)` | Reads and increments the current tracked field-only scalar in declaration order |
| `ExpectVersion<int>("revision", ...)` | Checks each item's caller-supplied original token; a stale later item restores earlier assignments |
| `SetProperty<int>("revision", ...)` + `AllowWrite<int>(...)` | Separately configured advancement of an application-managed token |
| `ProtectWrite<string?>("review remark")` + command permission | Explicit mapped write protection; null is an assignment, not omission |
| `Save().ReturnsSaved(...)` | One shared Save, then a fresh complete-key query returning DTOs in input order |
| `Stage()` | Keeps saved rows unchanged until the caller's later shared Save |
| `MapInMemoryWrites(..., useMappedPropertyState: true)` | Independent complete fixture-state permission; read bindings alone never grant write state |

The [EF/model-free program](Program.cs) checks later-item conflict restoration, conditional omission,
nullable indexer values, input order, staged batches, one shared Save, checkpoints and baselines.
Caption omission still advances the counter/token and assigns the remark: that is the reviewed
designer recipe, not an inferred convention.

This is bounded tracked row mapping, not native bulk SQL or a promise of one database round trip.
Only explicit non-identity mapped targets are added. Matching/primary/alternate/foreign/unique keys,
generated columns, graphs and native/atomic targets keep their existing guards. Fixtures do not
simulate SQL constraints, tracker internals, generated rowversions or races. Defaults are unchanged.

Run: `dotnet run --project MappedBatchSetters.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
See [mapped batch setters](../../../docs/implemented/writes/MAPPED-BATCH-SETTERS.md).
