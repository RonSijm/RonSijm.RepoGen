# ScriptedStreams: cold async-stream test doubles

Start with [ScriptedFeedRepositoryDesigner](Configuration/ScriptedFeedRepositoryDesigner.cs).
It declares one read-only streamed query and opts into test output; no write contract is generated.
[Program.cs](Program.cs) is a small EF-free consumer demonstrating enumeration behavior.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs this designer with
[ScriptedFeedContext](Model/ScriptedFeedContext.cs).

| Designer declaration | Generated result |
| --- | --- |
| `UseClosedRepositories(...)`, `WriteEnabled = false` | [IReadFeedEntryRepository and combined read-only facade](../Contracts/Generated/ScriptedFeedRepositories.g.cs); no write port |
| `HasQuery("Watch").Where<int>(...).OrderBy(...).ProjectTo(...).Stream()` | `IAsyncEnumerable<FeedItem> WatchAsync(int afterId, CancellationToken ...)` in the contract, with a SQL implementation in [Persistence/Repositories.g.cs](Persistence/Repositories.g.cs) |
| `UseScriptedTestDoubles(...)` | [FeedEntryRepositoryTestDouble](Generated/TestDoubles.g.cs) with typed `OnWatchAsync` and `OnWatchAsyncArguments.AfterId` |

## Behavior shown by Program.cs

Creating a stream does not invoke its handler. Each enumeration invokes it separately, starting at
the first `MoveNextAsync`. Recorded calls stay `InProgress` while yielding, become `Completed`
on full exhaustion and disposal, or `Disposed` on early disposal. Failures and cancellation have
separate outcomes. Both the operation token and `.WithCancellation(...)` token are honored.

`ReturnsItems(...)` captures the outer item sequence when arranging. Mutable items are **not** deep-copied;
use handlers returning detached objects when the test needs that policy. `RecordCalls(...)` also
requires an explicit argument snapshot adapter.

The designer's `entry.Id > afterId` predicate and sort are **not executed by the script**.
All items/results/failures are explicitly arranged. Keep provider-backed tests for SQL and actual
database streaming behavior. The executable has no EF, context, Models or designer dependency.

## Run

```powershell
dotnet run --project samples/ConfigurationExamples/ScriptedStreams -c Release
```

Regenerate with [Generate.ps1](../Generate.ps1), not by running the consumer. Production source is
excluded from this consumer's compilation and independently validated by the runner.
See [scripted test doubles](../../../docs/implemented/testing/SCRIPTED-TEST-DOUBLES.md) for the complete API.
