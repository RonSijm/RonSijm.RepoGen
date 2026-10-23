# Ledger: optional advanced infrastructure

Keyed context selection, versioned commands, unit of work, scoped caching and atomic writes.

## Start here

1. [LedgerRepositoryDesigner.cs](Configuration/LedgerRepositoryDesigner.cs) — what RepoGen generates.
2. [LedgerContext.cs](Model/LedgerContext.cs) — the EF context and entity model.
3. [LedgerContracts.cs](Contracts/LedgerContracts.cs) — handwritten application types and ports, not generated code.
4. [Program.cs](Program.cs) — setup, repository usage and executable assertions.
5. [Generated](Generated) — checked-in generator output; do not edit it manually.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs `LedgerContext` with this designer and selects
**per-contract layout**; that runner choice explains why the filenames differ from Simple:

```csharp
Generate(ledger, new LedgerRepositoryDesigner().CreateModel(), "Ledger",
    contracts: "Contracts/Generated/Ledger", layout: RepositoryOutputLayout.PerContract,
    apiBaseline: "Contracts/ApiBaselines/LedgerApi.json");
```

| Generated file | What to find in it |
| --- | --- |
| [IReadLedgerEntryRepository.g.cs](../Contracts/Generated/Ledger/RonSijm.RepoGen.ConfigurationExamples.Contracts.Ledger.Generated.IReadLedgerEntryRepository.g.cs) | Four declared read methods: `GetEntriesAsync`, `GetCachedEntriesAsync`, `GetPageAsync`, `GetAfterAsync` |
| [IWriteLedgerEntryRepository.g.cs](../Contracts/Generated/Ledger/RonSijm.RepoGen.ConfigurationExamples.Contracts.Ledger.Generated.IWriteLedgerEntryRepository.g.cs) | Three declared commands: `EnsureEntryAsync`, `PatchEntryAsync`, `StageDeletionAsync` |
| [ILedgerEntryRepository.g.cs](../Contracts/Generated/Ledger/RonSijm.RepoGen.ConfigurationExamples.Contracts.Ledger.Generated.ILedgerEntryRepository.g.cs) | Combined interface inheriting both generated ports |
| [LedgerEntry.Repository.g.cs](Generated/RonSijm.RepoGen.ConfigurationExamples.Models.Ledger.LedgerEntry.Repository.g.cs) | Internal `LedgerEntryRepositoryOperations`, query/command implementations and cache decorator |
| [RepositoryInfrastructure.g.cs](Generated/RepositoryInfrastructure.g.cs) | Shared helpers and `LedgerContextRepositoryRegistration` |
| [manifest.json](Generated/manifest.json) / [RepositoryApi.json](Generated/RepositoryApi.json) | Keyed lifetime/command inventory and API candidate compared with the approved baseline |

| Declaration/input | Specific generated effect |
| --- | --- |
| `UseClosedRepositories(...)` without a bound handwritten port | RepoGen declares the named read/write method signatures itself, unlike Reports/Adapters |
| `UseKeyedContextLeases<string>()` / per-operation `UseContextKey(...Database)` | Internal operation wrapper selects its keyed context from the application request; no context-selector expression appears in the public contract |
| `HasVisibilityPolicy("Live", entry => !entry.IsDeleted)` | Generated query roots exclude deleted entries |
| `HasQuery("GetCachedEntries")...Cache(...)` / `UseClosedCaching()` | `GetCachedEntriesAsync(LedgerRequest, ...)` uses the scoped cache infrastructure; `GetEntriesAsync` remains uncached |
| `HasQuery("GetPage")...Paged(Serializable)` / `HasQuery("GetAfter")...CursorPaged<int>(...)` | `GetPageAsync` returns `PagedResult<LedgerRow>`; `GetAfterAsync` returns `CursorPagedResult<LedgerRow, int>` with declared ordering/seek |
| `HasCommand("EnsureEntry")...InsertIfAbsent(...Code).Returns(...)` / `UseAtomicWrites<SqliteAtomicWriter>()` | `EnsureEntryAsync(CreateLedgerEntry, ...)` returns `CommandResult<LedgerRow>` through the selected atomic writer |
| `HasCommand("PatchEntry")...SetPropertyWhen(...).ExpectVersion(...).Save().Returns(...)` | `PatchEntryAsync(PatchLedgerEntry, ...)` implements optional-field/version handling and returns `CommandResult<LedgerRow>` |
| `HasCommand("StageDeletion")...TrackedDelete().Stage()` | `StageDeletionAsync(DeleteLedgerEntry, ...)` returns `CommandResult`, staging deletion in the selected work; persistence remains an explicit unit-of-work save |

Open `LedgerEntry.Repository.g.cs` and search for **`LedgerEntryRepositoryOperations`** to see
the internal orchestration. The keyed provider, cache adapter, unit-of-work registration and save
participant in [Program.cs](Program.cs) are handwritten runtime infrastructure, not generated
from the designer. The [approved LedgerApi.json](../Contracts/ApiBaselines/LedgerApi.json) is also
an input; regeneration only compares it and never overwrites it.

## Run

From the RepoGen repository root, using the checked-in generated code:

```powershell
dotnet run --project samples/ConfigurationExamples/Ledger -c Release
```

After changing inputs, regenerate with `./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`
from the repository root. Running `Program.cs` does not run the designer or regenerate repositories.

## What this example demonstrates

[LedgerRepositoryDesigner](Configuration/LedgerRepositoryDesigner.cs) starts simple and explicitly enables
closed CQRS, request-to-database keys, scoped result caching, SQLite atomic insertion and a timeout.
Every operation binds its request key. A visibility policy hides deleted rows. The DTO is a
member-initializer record projection so subsequent result ordering/seeking remains SQL-translatable.

The application calls `EnsureEntryAsync`, `PatchEntryAsync`, `GetCachedEntriesAsync`, `GetPageAsync`,
`GetAfterAsync`, and `StageDeletionAsync` through EF-free read/write contracts. It supplies neither
entities nor arbitrary query/write expressions. `Optional<string?>` distinguishes omitted and null;
original version input produces an honest conflict. Computed uppercase code/ID come from the database.

An application-owned keyed provider whitelists two in-memory databases and supplies their actual
cache scopes. A scoped keyed work retains one context, rejects key switches and coordinates observers.
The process-wide memory adapter partitions equal requests, invalidates after save, bypasses reads
inside transactions and preserves entries on rollback. A rolled-back work is not reused.

Two later scopes explicitly register an infrastructure [save participant](../../../docs/implemented/repositories/SAVE-PARTICIPANTS.md)
before the keyed work's first save. Its small receipt row shares the generated patch's tracked save
and real commit/rollback boundary. The receipt has no generated repository because it is not in the
designer allowlist. Earlier scopes remain unconfigured, and all fifty generated inventory files and
the approved baseline remain unchanged by this optional runtime example.

Ledger uses per-contract output: shared infrastructure, entity implementation, individual EF-free
read/write/combined interfaces, manifest and a candidate API snapshot. Its initial checked-in
`Contracts/ApiBaselines/LedgerApi.json` is only read, never rewritten by regeneration. Breaking
changes fail with exit 4; missing baselines also fail in check mode. Intentional changes need an
explicit reviewed baseline update. See [API compatibility](../../../docs/implemented/generation/API-COMPATIBILITY.md).

Its project imports an **off-by-default** pre-compile `--check` target; after building the
runner, enable it with `-p:RepoGenCheckOnBuild=true`. The target detects drift, never regenerates.
See [Code-first generation](../../../docs/implemented/generation/CODE-FIRST-GENERATION.md), [Typed commands](../../../docs/implemented/writes/TYPED-COMMANDS.md)
and [Scoped caching](../../../docs/implemented/repositories/SCOPED-CACHING.md) for behavior/limitations. None of these registrations
or packages are required by Simple.

## Why these files are not compiled into the consumer

The files are kept together for reading, but the shared [Designers](../Designers/Designers.csproj),
[Models](../Models/Models.csproj) and [Contracts](../Contracts/Contracts.csproj) projects compile their
respective inputs. [ExampleSources.props](../ExampleSources.props) excludes those inputs from this
consumer's compilation. This preserves the design-time bootstrap and EF-free contract boundary;
the consumer does not reference the Designers project.

[All examples and suggested reading order](../README.md).
