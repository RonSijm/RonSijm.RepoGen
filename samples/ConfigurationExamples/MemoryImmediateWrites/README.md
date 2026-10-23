# MemoryImmediateWrites: opt-in update/delete/consume simulation

Start with [DispatchQueueRepositoryDesigner](Configuration/DispatchQueueRepositoryDesigner.cs).
It opts four existing production commands into scalar fixture execution. The [consumer](Program.cs)
uses EF-free ports and immutable public seeds, not the internal persistence model or designer.
The small queue table illustrates generic selection/mutation; no dispatch or retry policy is generated.

## Designer → generated output

[GenerationRunner](../GenerationRunner/Program.cs) explicitly pairs
[DispatchQueueContext](Model/DispatchQueueContext.cs) with the designer and chooses these four files:

| File | What this designer creates |
| --- | --- |
| [CQRS contracts](../Contracts/Generated/DispatchQueueRepositories.g.cs) | `IReadQueuedEnvelopeRepository`, `IWriteQueuedEnvelopeRepository` and combined interface; application DTOs only |
| [Test backend](Generated/TestDoubles.g.cs) | `DispatchQueueContextRepositoryFixture`, shared public store/write view, `QueuedEnvelopeRepositoryTestDouble` and typed scripts |
| [Production implementation](Persistence/Repositories.g.cs) | Actual EF Update/Delete and SQLite returning commands, plus tracked StageBody; excluded from this executable |
| [Manifest](Generated/manifest.json) | Per-operation coverage and explicit `immediateSimulation` non-relational receipts |

The declarations produce these APIs:

| Declaration | Emitted method/result |
| --- | --- |
| Store + complete scalar read/write maps | `QueuedEnvelopeStore`; no persistence constructor or reflection mutation |
| `HasQuery("List")...ProjectTo<EnvelopeView>().List()` | `ListAsync(request)` with named projection, visibility and designer sorting |
| `HasCommand("Requeue")...Update(...).ReturnsMany(...).SimulateImmediateWrites()` | `RequeueAsync(request)` → `MutationResult<EnvelopeView>` with updated rows |
| `HasCommand("Consume")...ExpectVersion(...).Delete().ReturnsMany(...).SimulateImmediateWrites()` | `ConsumeAsync(request)` → removed rows, empty for no match |
| `HasCommand("ClearQueue")...Delete().WithOutcome().SimulateImmediateWrites()` | `ClearQueueAsync(request)` → Applied/count or NotMatched |
| `HasCommand("MarkAttempt")...Update(...).SimulateImmediateWrites()` | `MarkAttemptAsync(request)` → immediate matched count |
| `HasCommand("StageBody")...Patch(...).Stage()` | `StageBodyAsync(request)` → staged state; shared Save remains separate |

Requeue sets both new and previous attempt counts. Every setter reads the original saved row;
PreviousAttempts does not accidentally receive the incremented value. Consume compares the saved
input version and removes/returns the matching row once. Neither flushes staged writes. Return order
is unspecified.

## What the consumer checks

Program arranges four rows and a baseline, then verifies visibility/queue filters, original-row setters,
immediate returned values, an unrelated staged row remaining unsaved until shared Save, stale-version
no-match, consume-once behavior, checkpoint rollback, count/outcome variants and baseline restore.
The generator tests separately share cases with actual generated SQLite repositories and cover binary
versions, pending conflicts, compound keys, partitions and atomic failures. This executable itself does
**not** run SQLite.

## Optional by design

Ordinary users need none of the testing flags/stores/maps. Even with automatic fixture commands enabled,
immediate operations require per-command `SimulateImmediateWrites()`; use `false` to disable it. Production
returning output still needs a selected writer, here `SqliteReturningWriter` at design time only.
The consumer depends on Contracts, Abstractions, Testing and DI, not EF, Models, Designers, Design,
Generation, Tool, Validation or SQLite.

Fixtures do not prove SQL constraints, trigger behavior, locking or competing consumers. Native upserts,
dependent cascades and graph writes retain script/real-provider boundaries. See the
[semantics guide](../../../docs/implemented/testing/INMEMORY-IMMEDIATE-WRITES.md), including why immediate writes can
conflict with a staged row's later save.

## Generate and run

From the repository root:

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Run
dotnet run --project samples/ConfigurationExamples/MemoryImmediateWrites -c Release
```
