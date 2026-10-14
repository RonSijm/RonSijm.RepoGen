# MemoryAtomicWrites: explicit fixture insert/upsert

Start with [ReservationDirectoryRepositoryDesigner](Configuration/ReservationDirectoryRepositoryDesigner.cs).
It declares native production insert-if-absent/upsert commands, then opts each into saved-state fixture
simulation. [Program.cs](Program.cs) only consumes generated CQRS ports and arranges/asserts public rows.
It does not discover a designer or define repository behavior.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs this designer with
[ReservationDirectoryContext](Model/ReservationDirectoryContext.cs) and emits exactly four artifacts:

| Output | Responsibility |
| --- | --- |
| [ReservationDirectoryRepositories.g.cs](../Contracts/Generated/ReservationDirectoryRepositories.g.cs) | EF-free IReadReservationSlotRepository / IWriteReservationSlotRepository |
| [TestDoubles.g.cs](Generated/TestDoubles.g.cs) | Exact ports, scripts, public mapped fixture and insert/upsert handlers |
| [Repositories.g.cs](Persistence/Repositories.g.cs) | Actual production operations using the explicitly selected SQLite atomic writer |
| [manifest.json](Generated/manifest.json) | Testing-only atomicSimulation coverage and explicit non-relational receipts |

The consumer [project](MemoryAtomicWrites.csproj) excludes production/Model/Configuration inputs.
It references Contracts, Testing and DI only. Models and Designers compile the handwritten inputs
separately; the runner never requires generated persistence to bootstrap.

| Designer declaration | Emitted API / behavior |
| --- | --- |
| UseClosedRepositories + HasQuery("List")...ProjectTo...List | IReadReservationSlotRepository.ListAsync(SlotRequest) |
| Create...InsertIfAbsent...Returns + SimulateImmediateWrites | IWriteReservationSlotRepository.EnsureAsync(SlotRequest), CommandResult&lt;SlotView&gt; |
| Create...InsertOrUpdate...ExpectVersion...Returns + SimulateImmediateWrites | PutAsync(SlotRequest): Inserted / Updated / NotMatched |
| HasInMemoryStore + MapInMemoryEntity + MapInMemoryWrites | ReservationSlotStore holds immutable public SlotSeed rows, without constructing internal ReservationSlot |
| HasInMemoryGeneratedValue(Id, factory) | Explicit seed-aware fixture allocation only on an actual insert |
| HasVisibilityPolicy("Visible") | Hidden rows still cause conflicts; only the upsert update branch is filtered |
| HasCommand("StageValue")...Patch...Stage | StageValueAsync retains pending tracked values; immediate commands never save them |

## What the executable demonstrates

- An insert generates an explicit fixture ID and maps null saved metadata into a public arrangement row.
- Repeating the conflict target reports AlreadyExists, returns no value and changes nothing.
- A hidden saved target is AlreadyExists on Ensure, NotMatched on Put, never a second inserted row.
- A matching upsert preserves identity and increments the original version; stale versions are NotMatched.
- Unrelated earlier staging remains pending and can be saved deliberately afterward.
- Checkpoint disposal restores both inserts and updates; baselines restore the original saved rows.
- The complete (Scope, Code) conflict target permits independent scopes without inventing tenancy rules.

## Optional, not SQL emulation

All fixture settings are off by default. Suitable native commands additionally require
`SimulateImmediateWrites()` individually; false removes that opt-in. The selected production writer
is still required for valid generation but is not a dependency of this fake-only application.

Insert inputs must cover every ordinary scalar; database/CLR defaults are not inferred. Explicit factories
see saved rows, not pending reservations. Max + 1 is not SQL IDENTITY/AUTOINCREMENT and can reuse values
after reset/deletion. Expected versions/visibility only constrain existing-row updates, not insertion.
Returned-value construction and mappings complete before the simulated commit.

No SQL locking, race protection, collation, triggers, constraints, caching or durability is promised.
Actual generated SQLite/fake contract tests compare suitable operations using explicitly supplied IDs;
they do not assert identical database sequence allocation. Graph writes remain outside this scalar slice.
See [the full simulation contract](../../../docs/implemented/testing/INMEMORY-ATOMIC-WRITES.md).

From the repository root:

```powershell
.\samples\ConfigurationExamples\Generate.ps1 -Configuration Release -Check -Run -ValidateCompilation
dotnet run --project samples/ConfigurationExamples/MemoryAtomicWrites -c Release
```
