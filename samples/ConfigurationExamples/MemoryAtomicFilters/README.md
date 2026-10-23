# MemoryAtomicFilters: filtered upserts have different branches

Start with [FilteredReservationRepositoryDesigner](Configuration/FilteredReservationRepositoryDesigner.cs).
It uses the [context's soft-delete filter](Model/FilteredReservationContext.cs) and a separately
configured hidden-row visibility policy. Services call closed named methods; they do not build
query chains or configure fixture behavior at runtime.

## Designer → generated output

| Designer declaration | Result |
| --- | --- |
| `UseAtomicWrites<SqliteAtomicWriter>()` | [Production operations](Persistence/Repositories.g.cs) use the selected unique-conflict writer and model-filtered EF update/reload |
| Public store plus read/write maps | [Generated fixture](Generated/TestDoubles.g.cs) uses detached [FilteredSlotSeed](Contracts/FilteredSlotSeed.cs) values, not EF model instances |
| `SimulateImmediateWrites().UseInMemoryModelQueryFilter()` on each atomic command | Unfiltered conflicts/inserts, filtered existing-row updates and model-only updated result checks |
| `InsertIfAbsent(row => new { row.Scope, row.Code })` | `EnsureAsync` reports existing composite conflicts even when the stored row is deleted/hidden |
| `InsertOrUpdate(...)` + expected version | `PutAsync` preserves the existing ID/conflict fields, returns updated scalars and detects stale tokens |
| `ArchivePut` updates the soft-delete flag | A now-hidden model result fails before fixture commit; production reload behavior is not weakened |
| `StageValue` alongside atomic operations | Pending state remains; a native version change can conflict with later shared Save |
| Bound read/write ports | [Contracts](../Contracts/Generated/FilteredReservationRepositories.g.cs) expose EF-free named application methods |

[Program.cs](Program.cs) checks hidden conflicts, an inserted deleted DTO, existing-key retention,
filtered result failure, retained staging, a token conflict and checkpoint rollback.
[manifest.json](Generated/manifest.json) records branch/filter application without claiming SQL
constraints, races or transaction equivalence.

The consumer references contracts and fixture runtime only: not EF, Models, Designers or SQLite.
The runner explicitly pairs this designer with `FilteredReservationContext` and emits the four
linked outputs above.

## What remains optional?

Atomic writer capability, stores, automatic reads/writes, model-filter consent and each command's
immediate simulation are independent. Remove either command consent to require a typed script.
Entity read permissions do not enable writes. Normal simple/unfiltered examples receive no new output.

An operation-owned SQL transaction rolls back a filtered result failure. A caller-owned SQL
transaction can still contain the update until the caller rolls it back; fixture failures remain
pre-commit. This is a deliberate boundary, not a database emulation claim. See the
[guide](../../../docs/implemented/testing/INMEMORY-ATOMIC-FILTERS.md) for that distinction and the
remaining graph/provider boundaries.

## Generate and run

From the repository root:

```powershell
pwsh -NoProfile -File samples/ConfigurationExamples/Generate.ps1 -Configuration Release -ValidateCompilation -Run
dotnet run --project samples/ConfigurationExamples/MemoryAtomicFilters/MemoryAtomicFilters.csproj -c Release --no-build
```
