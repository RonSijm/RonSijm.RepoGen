# MemoryNativeResults: bounded fresh graphs after native updates

Start with [QueueResultsRepositoryDesigner](Configuration/QueueResultsRepositoryDesigner.cs).
The [two-table context](Model/QueueResultsContext.cs) contains tickets, their queue and an original
revision token. Named commands update matching tickets in one native mutation, then read a fresh
DTO for every directly updated primary key before the operation completes.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/QueueResultsRepositories.g.cs) | Bound writer and queue-name reader; typed requests and DTOs only |
| [Production operations](Persistence/Repositories.g.cs) | Native update followed by a bounded untracked complete-key query inside the transaction/savepoint |
| [Fixture implementation](Generated/TestDoubles.g.cs) | One prepared root overlay, saved-only related tables and complete-key result correlation before publication |
| [Manifest](Generated/manifest.json) | Native saved-query receipt, explicit maximumRows, rollback and non-provider fixture semantics |

| Designer declaration | Generated effect |
| --- | --- |
| UseReturningWrites<SqliteReturningWriter>() | Explicit native Update capability, not a default dependency |
| Update(...) + ReturnsSavedMany(..., maximumRows: 20) | Fresh queue/member DTOs for every directly updated row |
| maximumRows: 1 on RelabelAtMostOne | Rejects and rolls back the whole operation if more than one row changes; does not use Take(1) |
| ExpectVersion(...) | Native selection uses the original revision; fresh result selection uses complete saved identity |
| Public read/write maps + declared navigations | Independently configured fixture graph result behavior without persistence entities |
| SimulateImmediateWrites() | Explicit saved-only fixture mutation; no shared Save or pending-state flush |

The [consumer](Program.cs) checks the limit fails without a partial write, every DTO sees both
updated ticket labels, stale revisions return no data, and checkpoints/baselines restore saved
state. It does not reference EF, models, designers or native writers. Result order is unspecified;
the ordered Labels inside each DTO come from the designer's explicit projection sort.

`maximumRows` is an abort guard after native execution, not a SQL TOP/LIMIT or a claim that only
that many scalar rows will be buffered. A separate result query reads the updated roots and related
data. Failure rolls back an owned transaction or this operation's borrowed savepoint; Delete keeps
its pre-delete scalar result mode. SQL constraints, identities, locks and races remain provider tests.

Run: `dotnet run --project MemoryNativeResults.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
See [bounded native saved results](../../../docs/implemented/writes/NATIVE-SAVED-RESULTS.md).
