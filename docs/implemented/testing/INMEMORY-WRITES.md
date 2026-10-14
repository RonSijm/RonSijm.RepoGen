# Optional stateful fixture writes

`UseInMemoryTestCommands()` enables a bounded, EF-free simulation of the same **designed** tracked
Create/Patch/TrackedDelete and [bounded root-row batch operations](INMEMORY-BATCHES.md) that production repositories execute. It is off by default and
independent of `UseInMemoryTestQueries()`. Services retain named operations and `IRepositoryUnitOfWork`;
no additional service-side mutation DSL is introduced.
Suitable immediate scalar Update/Delete and returning/consume commands are separately opt-in through
[SimulateImmediateWrites](INMEMORY-IMMEDIATE-WRITES.md); they execute saved rows without flushing pending writes.
Suitable InsertIfAbsent/InsertOrUpdate have their own [saved-state fixture semantics](INMEMORY-ATOMIC-WRITES.md)
under the same explicit opt-in. Native SQL allocation, uniqueness/locking/races and provider behavior are not inferred.

Start with [DraftWorkspaceRepositoryDesigner](../../../samples/ConfigurationExamples/MemorySharedWrites/Configuration/DraftWorkspaceRepositoryDesigner.cs)
and its [exact designer → generated-output map](../../../samples/ConfigurationExamples/MemorySharedWrites/README.md).

## Configure in the designer

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Generated")
    .UseScriptedTestDoubles("Tests.Generated")
    .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
    .RequirePortableTestCommands());

entity.HasInMemoryStore(row => row.Id, row => new Draft
    { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden });
entity.HasCommand("StageRename").Where<DraftRequest>((row, request) => row.Id == request.Id)
    .Patch(setters => setters
        .SetProperty<string, DraftRequest>(row => row.Title, (row, request) => request.Title)
        .SetProperty(row => row.Version, row => row.Version + 1))
    .AllowWrite(row => row.Version)
    .ExpectVersion<int, DraftRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
    .Stage();
```

Create requires caller-supplied keys/values (`ValueGeneratedNever`) or [explicit generated-value adapters](INMEMORY-GENERATED-VALUES.md).
Patch/Delete can also operate on known existing generated-on-insert values. Update generation requires
an explicit adapter; no values are inferred. Concurrency tokens use declared ordinary scalar/binary state
(`IsConcurrencyToken`); an explicit generated-token factory is only a fixture simulation, not evidence of
database rowversion/race behavior. Copy/key adapters must preserve the complete mapped scalar state and key,
return detached roots and deep-copy mutable scalars. Patch values are copied **before staging**, not just at Save, so later caller mutation
doesn't change pending state when the adapter deep-copies those values.

Production guards, scalar assignments, conditional setters, expected versions, visibility/bypasses,
context-key selection, protected-write permissions and result projections are reused. They are not
reconfigured on the fake. `RequireTestOverride(reason)` on a command acknowledges a required script;
the reason is not a generated implementation.

## Pending changes versus saved rows

Generated fixture data derives from `InMemoryWriteDataContext`, implementing `IRepositoryUnitOfWork`.
Repositories in one fixture partition share it. Registration does **not** replace an application's global
unit of work: the consumer deliberately selects its context/partition, for example:

```csharp
services.AddDraftWorkspaceContextRepositoryTestDoubles();
services.AddScoped<IRepositoryUnitOfWork>(provider =>
    provider.GetRequiredService<DraftWorkspaceContextRepositoryFixture>().Data);
```

With typed context keys, register/use the explicitly selected `fixture.For(contextKey)` instead.
Saving one partition never saves another, even when both contain the same entity/key.

- Stage returns `Staged` without a saved value or store-row change. `PendingCount` counts **rows**, not
  calls. Multiple patches accumulate on one row; detached named reads/inspection see saved rows only.
- Write selection matches saved rows, then uses pending values for that row. A staged create is not a
  query root before Save. Selection must match at most one row; ambiguity fails explicitly.
- Expected tokens compare with original tracked state, not a version incremented by a previous staged
  patch. Wrong input returns `Conflict` without modifying pending state. Scalar/binary equality is
  ordinary CLR/structural equality, not SQL collation or EF custom comparison.
- Shared Save prepares every touched store before committing any. Adapter/comparison/setter failure
  and pre-commit cancellation cannot partially save other repositories. Only approved assigned columns
  merge into saved rows; stale unassigned columns are not overwritten.
- Unsupplied conditional assignments return `NoChanges`. Unchanged assignments without an expected
  version can save zero rows. An expected version forces a conditional write even when values match.
- Save checks row existence and original tokens. A conflicting command returns `Conflict`; another
  pending row's conflict raises `RepositoryWriteConflictException`, preserving its pending state for
  explicit resolution. Direct shared Save wraps the same underlying fixture conflict exception.
- Creates use explicit supplied keys; duplicates fail rather than inventing success or replacing data.
  The optional [fixture-key comparer](INMEMORY-KEY-COMPARERS.md) controls pending/saved key identity,
  including mapped write views. It does not rewrite command predicates, batch matching or scalar/token checks.
  `.Save()` saves **all** pending repositories in the partition. Its `AffectedRows` is that shared count.
  Result DTOs are detached. A mapping failure raises `RepositorySavedResultException` **after** Save:
  don't blindly retry; an active transaction may still roll back.

`DiscardChanges()` drops pending writes without reverting saved rows. Capturing/restoring baselines or
clearing all fixture data requires no pending writes. Outside a transaction, manual Seed/Replace/Remove
can arrange changed tokens/missing rows to test save conflicts; these aren't production operations.
ResetTestDouble resets scripts/history, not pending/saved data. Matching scripts/handlers override writes.

## Checkpoint transactions

`BeginTransactionAsync()` captures detached saved rows across the fixture partition. It requires no
pending writes, rejects nesting and accepts only `IsolationLevel.Unspecified`. SQL isolation levels
must not silently become fake guarantees. Save inside a transaction is visible to that fixture's reads.

Rollback or disposal without commit restores all captured stores and discards transaction pending
writes. Commit releases the checkpoint; it **does not implicitly Save** staged entries. Arrangement,
store creation and baseline resets are rejected inside a transaction. Disposing fixture data rolls back
its active transaction, drops pending entries and rejects later access. Baselines/store instances stay
independent of checkpoints.

This is scoped/shared fixture state, without ambient async ownership or parallel isolation. Don't use
one partition concurrently for independent transactions. It is not evidence of provider locks,
durability, concurrency races or distributed transactions. Use [real-provider fixtures](REPOSITORY-CONTRACT-FIXTURES.md)
for those guarantees.

## Capability boundary and coverage

Use the same public ordinary entity/store type, or declare separate complete
[public scalar read/write adapters](INMEMORY-PROXY-WRITES.md) for internal models. The fixture key directly selects the complete
EF primary key in declared order; composite keys need an explicit immutable key shape. Mapped state
needs public scalar getters and ordinary primitive/enum/string/decimal/Guid/date/time/binary types;
entity-shaped patch fields need public non-init setters. Constructors, getters/setters and adapters must be suitable
for pure fixture execution. Entity-shaped business behavior isn't neutralized; mapped public writes use
plain scalar slots and never instantiate the persistence entity.

Typed scripts or real-provider fixtures are still required for:

- Generated keys/defaults/computed/token values without required explicit factories, or ReloadAfterSave.
  [Deterministic scalar factories](INMEMORY-GENERATED-VALUES.md) are supported; generation is never inferred.
- Arbitrary graph reconciliation and inferred proxy reverse maps. Suitable designed leaf-child
  reconciliation has a separate [SimulateCollectionWrites opt-in](INMEMORY-COLLECTIONS.md) with selected
  child stores and explicit scalar maps/factories. Scalar `Returns` results or explicit
  [ReturnsSaved](../writes/SAVED-COMMAND-RESULTS.md) fresh navigation DTOs have distinct result semantics. Suitable
  bounded PatchMany/leaf DeleteMany are supported; see [batch semantics and limits](INMEMORY-BATCHES.md).
  Scalar immediate Update/Delete/returning writes require the separate [per-command simulation opt-in](INMEMORY-IMMEDIATE-WRITES.md), not automatic provider parity.
  Scalar InsertIfAbsent/InsertOrUpdate likewise require [explicit simulation](INMEMORY-ATOMIC-WRITES.md),
  complete insert scalars/factories and eligible stores; their actual SQL/constraint behavior is not simulated.
- Deletion of rows with dependent relationships: scalar removal must not pretend to perform
  cascades, relationship fixup or child-store updates.
- Navigation-dependent writes, unmapped private entities, shadow/backing-field state, owned/shared/inherited shapes,
  unconfigured/context-dependent EF filters, filtered graphs/AutoInclude, custom scalar
  conversions/comparers and provider collection types. Eligible scalar commands can separately
  opt into [portable final-model filters](INMEMORY-MODEL-WRITE-FILTERS.md), independently of reads.
- SQL constraints, caching/commit observers, retries, locking semantics and external effects. Additional
  Save tags are validated, not used to simulate caching; RecordChangesAsync is unsupported.

RGD3401 validates prerequisites; RGD3402 rejects uncovered selected commands in strict mode unless an
explicit RequireTestOverride acknowledges a script. Unsupported non-strict calls fail with their operation
and coverage reason, including empty stores. Disabling the backend leaves prior output intact.

Per-operation coverage and `writeSimulation` manifest receipts describe pending/saved state, shared Save,
explicit partition selection, transaction limits and uninferred values. Testing-only command models don't
enter production API/cache fingerprints. Shared cases compare named writes, Stage/Save and rollback with
actual generated SQLite repositories, supplementing rather than replacing provider integration tests.

Optional [per-command scalar-call translations](INMEMORY-COMMAND-TRANSLATIONS.md) reuse registered
fixture behavior in supported selection/guards/setters/batches/collections and returned DTOs.
They are off per command, independent of reads, and never relax the structural/permission boundaries
above or rewrite store/copy/read-map/write-map/generated-value adapters.

[HasInMemoryValueEquality](INMEMORY-VALUE-EQUALITY.md) separately declares tracked state/version
comparison for ordinary non-identity scalars with compatible CLR value-comparer metadata. Queries,
keys/FKs, generated/binary tokens, conversions/provider comparisons and strict mapped round trips
remain separate; none of those contracts is relaxed by the policy.
