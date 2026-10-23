# MemoryAtomicCapture: native insert/upsert scalar results

Start with [CapturedReservationsRepositoryDesigner](Configuration/CapturedReservationsRepositoryDesigner.cs).
Its [one-table model](Model/CapturedReservationsContext.cs) has an ordinary unique Code, a computed
field-backed Label, a computed shadow Revision and an indexer Route default. The designer selects
native capture and independently declares public-data fixture state and value recipes.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/CapturedReservationsRepositories.g.cs) | Bound writer port; requests and scalar DTOs only |
| [Production operations](Persistence/Repositories.g.cs) | Native inserted buffer; filtered no-tracking updated scalar query; no tracker entries |
| [Fixture implementation](Generated/TestDoubles.g.cs) | Saved-only branch selection, original tokens, typed slots and explicit factories |
| [Manifest](Generated/manifest.json) | Capture capabilities and separate non-provider fixture receipts |

| Designer declaration | Generated effect |
| --- | --- |
| UseAtomicWrites<SqliteAtomicWriter>() | Explicit production unique-conflict provider, not a new default dependency |
| InsertIfAbsent / InsertOrUpdate + Returns | Inserted/Updated values; AlreadyExists/NotMatched without existing-row data |
| IncludeMappedProperties() | Exact field/indexer/shadow scalars in detached infrastructure buffers; ordinary keys/setters remain |
| ExpectVersion<int>("Revision", ...) | Conditional update using the caller's original token, not token assignment |
| Complete mapped write state + named factories | Independently authorized public fixture recipes, not inferred SQL behavior |
| SimulateImmediateWrites() | Saved-state fixture execution without flushing pending tracked operations |

The [consumer](Program.cs) checks insert/existing/update/stale outcomes, raw mapped values instead
of display getters, retained saved identity, checkpoints and baseline restoration. It has no EF,
model, designer or native-writer dependency.

All advanced choices remain optional. Production capture alone does not enable fixture execution.
The fixture does not simulate SQL locks, identities, rowversions, constraints or race guarantees.
An atomic mapping failure inside a caller-owned production transaction requires caller rollback;
the fixture prepares results before committing its saved rows.

Run: `dotnet run --project MemoryAtomicCapture.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
See [atomic scalar capture](../../../docs/implemented/writes/ATOMIC-SCALAR-CAPTURE.md).
