# MemoryGeneratedValues: explicitly designed fixture values

Start with [GeneratedNotebookRepositoryDesigner](Configuration/GeneratedNotebookRepositoryDesigner.cs).
The [model](Model/GeneratedNotebookContext.cs) declares a generated insert key, an insert default and a
computed scalar concurrency token. The designer separately declares **test-only** value factories.
No factory is guessed from SQL, an entity convention or a projection. The [consumer](Program.cs)
references only contracts, Testing and DI; it neither executes a designer nor loads EF.

## Designer → generated output

| Declaration | Exact output |
| --- | --- |
| Closed repositories + List/Create/StageCreate/StageRename/RenameMany | [IReadGeneratedNoteRepository / IWriteGeneratedNoteRepository](../Contracts/Generated/GeneratedNotebookRepositories.g.cs) |
| `HasInMemoryStore(row => row.Id, copy)` | [GeneratedNotebookContextRepositoryFixture and complete detached rows](Generated/TestDoubles.g.cs) |
| `HasInMemoryGeneratedValue(Id, ...checked(Max + 1))` | Seed/pending-aware insert allocation before Stage; overflow fails without reserving a row |
| `HasInMemoryGeneratedValue(Origin, ..."designer")` | An explicit fixture default when the input is the model's supported CLR sentinel; supplied non-null Origin survives |
| `HasInMemoryGeneratedValue(TextLength, ...Text.Length)` | Always-computed insert values and save-time update values, according to model generation/save metadata |
| `.Stage()` versus `.Save().Returns(...)` | Pending-only writes versus detached saved DTOs containing generated values |
| Expected TextLength + RenameMany | Original-token checks and generated batch DTOs in input order; no fake-specific service DSL |
| Strict automatic command coverage | Missing/unsupported factories fail generation rather than silently invent values |

[Production output](Persistence/Repositories.g.cs) is checked in and independently compiler-validated;
these factories do not appear there. The [manifest](Generated/manifest.json) lists adapter properties,
expressions, timing, allocation/reset semantics and the lack of database equivalence.

The executable checks saved defaults, supplied defaults, seeded/pending key reservations, shared save
counts, delayed update computation, stale tokens, ordered batches and rollback/baseline replay.
The computed TextLength is intentionally a simple scalar demonstration, **not** a recommended
monotonic rowversion: two different texts can have the same length. Use an appropriate production
concurrency token in real applications.

The allocation expression is consumer-owned policy. After rollback/baseline restore it derives its
next value from restored rows, and may reuse numbers after deletion. It is not SQL IDENTITY,
AUTOINCREMENT, trigger, Unicode length or race equivalence. Factories must be pure and non-reentrant;
external counters/clocks are not reset by the fixture. No graph/native/proxy write inference is added.

Everything is opt-in: `UseInMemoryTestCommands()` plus each explicit adapter. Without adapters,
unsupported generated writes still need scripts or a real-provider fixture. Removing/replacing the
store removes its adapters; `WithoutInMemoryGeneratedValue(property)` removes one.

Run from this folder: `dotnet run --project MemoryGeneratedValues.csproj`.
Regenerate/check using the [shared workflow](../README.md#generate-check-and-run).
See [generated-value semantics](../../../docs/implemented/testing/INMEMORY-GENERATED-VALUES.md).
