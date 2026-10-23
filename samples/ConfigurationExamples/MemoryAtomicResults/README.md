# MemoryAtomicResults: fresh DTO graphs from atomic commands

Start with [EnrollmentRepositoryDesigner](Configuration/EnrollmentRepositoryDesigner.cs).
Its [two-table model](Model/EnrollmentContext.cs) has enrollments with a unique course/code pair,
an ordinary primary key and an original version token. The designer declares each DTO query
and the separate permissions needed for portable fixture execution.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/EnrollmentRepositories.g.cs) | Bound writer with requests and DTOs only |
| [Production operations](Persistence/Repositories.g.cs) | ReturnsSaved reads by saved primary key, no tracking, before the atomic owned commit |
| [Fixture implementation](Generated/TestDoubles.g.cs) | Detached prepared root overlay and saved-only course/enrollment snapshots |
| [Manifest](Generated/manifest.json) | Separate result-query and fixture receipts, not native returned graphs |

| Designer declaration | Generated effect |
| --- | --- |
| UseAtomicWrites<SqliteAtomicWriter>() | Explicit native unique-conflict handling |
| InsertIfAbsent / InsertOrUpdate | Inserted/Updated results; AlreadyExists/NotMatched carry no existing-row data |
| ReturnsSaved(...) | Fresh DTO with course name and member list inside the operation transaction |
| ExpectVersion(...) | Update-only original-token predicate; the fresh read uses saved identity instead |
| Public data/read/write maps | Fixture arrangement and command state without persistence entities |
| HasInMemoryNavigation + UseInMemoryTestQueries | Explicit detached graph loading for the result read |
| SimulateImmediateWrites() | Explicit fixture atomic simulation; pending tracked changes are not flushed |

The [consumer](Program.cs) proves insertion includes the new root in its related member list,
upsert retains existing identity even when the request supplies another ID, and stale tokens
return no value. It also restores the saved baseline. No EF/model/designer/native-writer runtime
dependency reaches this application.

This is a separate result query, not a one-statement returned graph. Result failure rolls back
an owned production transaction; the owner must roll back a borrowed transaction. Fixtures
prepare the result before publication, without simulating database races, identities or locks.
No default repository output changes unless the designer opts in.

Run: `dotnet run --project MemoryAtomicResults.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
See [atomic saved results](../../../docs/implemented/writes/ATOMIC-SAVED-RESULTS.md).
