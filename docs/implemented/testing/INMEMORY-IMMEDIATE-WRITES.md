# Optional immediate fixture write simulations

`SimulateImmediateWrites()` on one designer command opts an ordinary scalar `Update()` or `Delete()`
into the generated in-memory backend, including `ReturnsMany` delete-and-return (consume) operations.
It is **off by default**. It changes no production command, application interface, cache fingerprint,
provider selection or ordinary user's dependencies. Services continue calling named operations.
The same opt-in now supports suitable InsertIfAbsent/InsertOrUpdate definitions; their
[saved-state insert/upsert contract](INMEMORY-ATOMIC-WRITES.md) describes branch-specific visibility,
conflict targets and explicit insert values/factories separately from this Update/Delete guide.

Start with [DispatchQueueRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryImmediateWrites/Configuration/DispatchQueueRepositoryDesigner.cs)
and its [exact designer → output map](../../../samples/ConfigurationExamples/MemoryImmediateWrites/README.md).
This service-test simulation does not establish SQL locking, uniqueness, trigger behavior, database
isolation or competing consumers executing atomically.

## Configure in the designer

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Generated")
    .UseReturningWrites<SqliteReturningWriter>() // Selected production returning capability.
    .UseScriptedTestDoubles("Tests.Generated")
    .UseInMemoryTestStores().UseInMemoryTestCommands()
    .RequirePortableTestCommands());

entity.HasInMemoryStore(row => row.Id, row => new Envelope
    { Id = row.Id, Queue = row.Queue, Body = row.Body, Version = row.Version });
entity.HasCommand("Consume")
    .Where<ConsumeRequest>((row, request) => row.Queue == request.Queue && row.Id == request.Id)
    .ExpectVersion<int, ConsumeRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
    .Delete().ReturnsMany(row => new EnvelopeView(row.Id, row.Body))
    .SimulateImmediateWrites();
```

The store must copy **all** ordinary mapped scalars, not just projected fields. Internal models can
instead use complete [public scalar read/write maps](INMEMORY-PROXY-WRITES.md); no entity construction
or reverse DTO mapping is inferred. The example demonstrates that EF/model-free form.

Production `ReturnsMany` still requires a returning writer; this testing option cannot make an invalid
production configuration valid. Count-only Update/Delete need no returning writer. Automatic reads
are independent: enable `UseInMemoryTestQueries()` only when wanted. `SimulateImmediateWrites(false)`
removes this opt-in. `RequireTestOverride(reason)` takes precedence, and matching scripts/handlers
still override eligible generated behavior.

## Results and saved-state semantics

| Designer command | Application result | Fixture behavior |
| --- | --- | --- |
| `Update(...)` / `Delete()` | `Task<int>` | Directly matched saved rows, including unchanged updates |
| `.WithOutcome()` | `Task<CommandResult>` | Applied/count or NotMatched for zero; never guesses absence versus conflict |
| `.ReturnsMany(mapping)` | `Task<MutationResult<T>>` | Detached updated/deleted scalar values; AffectedRows equals Items.Count |
| Tracked `.Patch(...)` / `.TrackedDelete()` | Existing CommandResult API | Separate [Stage/Save semantics](INMEMORY-WRITES.md); does not use this opt-in |

Execution selects current **saved** rows in the designed partition, reusing visibility/bypasses,
request guards, predicates and input versions. Explicit scalar/binary expected tokens use structural
equality, not byte-array reference identity. Other predicates retain CLR/LINQ-to-Objects semantics;
fixture key comparers do not rewrite them or imply SQL collation. SQL null compensation, overflow,
conversions and ordering can differ.

Every setter reads the **original saved row**, not earlier assignments. Setting `Attempts = row.Attempts + 1`
and `PreviousAttempts = row.Attempts` records the old count. Subsequent fixture reads see changes without
SaveChanges. Delete returns pre-deletion state and never generates values; Update returns post-update
state. No returned order is promised.

Target selection, setters, explicit factories, copy/key validation, public write encoders and returned-value
construction all finish before commit. Failure/pre-commit cancellation leaves all saved targets and existing
pending entries unchanged. A throwing DTO constructor cannot partly consume rows. This matches production
returning writes' mapping-before-commit boundary, unlike tracked `.Save().Returns(...)`, whose mapping can
throw `RepositorySavedResultException` **after** the shared save.

## Pending writes and checkpoints

Immediate operations neither flush nor refresh pending entries, just as production set-based writes bypass
EF's tracker. Staged creates are not targets. Earlier staged values/original versions are not discarded or
replaced. A later shared save can apply earlier approved assignments over the changed saved row, preserving
unassigned columns. It can also fail with `RepositoryWriteConflictException` when an immediate operation
removed its pending target or changed its original token. Pending entries remain for explicit resolution.

That conflict is intentional: mixing tracked and immediate operations is not transparently equivalent to
one tracked save. An explicit [fixture checkpoint](INMEMORY-WRITES.md#checkpoint-transactions) includes
immediate writes across stores. Commit retains saved changes; rollback/uncommitted disposal restores them
and clears pending state. Baselines also restore immediate changes when no pending writes remain.
Checkpoints provide **no parallel database isolation**.

## Explicit computed values

Suitable update-generated scalars require [HasInMemoryGeneratedValue](INMEMORY-GENERATED-VALUES.md).
For immediate Update, callbacks run for every matching row after ordinary setters are prepared. They receive
detached same-store candidates, including prepared ordinary updates and earlier computed values, not
unrelated staged assignments. Evaluation order is not a provider/sequence contract; prefer deterministic
row-derived factories. Pure/non-reentrant factory failure cannot partly commit. SQL expressions, sequences,
rowversions and trigger behavior are not inferred.

## Coverage and boundaries

Eligible commands get `Generated` coverage and an `immediateSimulation` receipt describing saved-only
targets, original-row setters, pending isolation, pre-commit preparation, unspecified order and
`relationalEquivalence: false`. Disabled/unselected commands emit no immediate backend/receipt.
Defaults leave existing examples unchanged. Strict uncovered commands fail with `RGD3402` unless an
explicit typed-script override is acknowledged. Ordinary production write protection still applies.

Suitable scalar native insert/upsert has its own [explicit fixture opt-in](INMEMORY-ATOMIC-WRITES.md).
Complete separately permitted [mapped scalar state](INMEMORY-MAPPED-RETURNING.md) additionally
supports exact shadow/field/indexer returned-value projections under the original production guards.
Scripts or real-provider fixtures remain necessary for unsupported native shapes, raw/temporal SQL, graph
reconciliation, dependent cascades, navigation-dependent writes, context-dependent/unconfigured
EF filters, filtered graphs/AutoInclude,
owned/shared/inherited or unmapped shadow shapes, custom scalar converters/comparers and generated-key changes.
Uniqueness/FK/check constraints, triggers, caches/commit observers, retries, durability, locks and races
are not simulated. Reads/writes use the fixture context's non-reentrant guard; adapters must be pure.

Scalar Update/Delete counts and native returned values may separately opt into
[per-command portable model filters](INMEMORY-MODEL-WRITE-FILTERS.md), in addition to
SimulateImmediateWrites. [Filtered native results](INMEMORY-NATIVE-FILTERS.md) retain matched
post-update/pre-delete values without a fresh result filter. Filtered atomic upsert and graph
semantics remain separate boundaries.

Tests cover entity/public/composite-key stores, partitions, scalar/binary versions, scripts, pending conflicts,
explicit factories, detached results, atomic failures/cancellation, checkpoints and resets. Shared cases
execute **actual generated SQLite** repositories, including failed returned-value construction rolling back
without losing prior staging. Use [real-provider fixtures](REPOSITORY-CONTRACT-FIXTURES.md) for database
guarantees; SQLite cases do not establish SQL Server OUTPUT/rowversion/locking correctness.
