# Explicit saved-state insert/upsert fixtures

`SimulateImmediateWrites()` now also opts suitable designed `InsertIfAbsent` and `InsertOrUpdate`
commands into optional in-memory execution. The production writer, port signatures and cache shapes
stay unchanged. This is saved-state **fixture simulation**, not a database atomicity or race guarantee.

See [ReservationDirectoryRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryAtomicWrites/Configuration/ReservationDirectoryRepositoryDesigner.cs)
and its [exact declaration-to-output example](../../../samples/ConfigurationExamples/MemoryAtomicWrites/README.md).
The fake consumer references neither EF, persistence models, designers nor a native provider writer.

## Configure at design time

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Contracts")
    .UseAtomicWrites<SqliteAtomicWriter>() // Required PRODUCTION capability.
    .UseScriptedTestDoubles("Tests.Generated")
    .UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands());

entity.HasCommand("Put")
    .Create<PutRequest>(request => new Entry
        { Scope = request.Scope, Code = request.Code, Value = request.Value, Version = 1 })
    .InsertOrUpdate(row => new { row.Scope, row.Code }, setters => setters
        .SetProperty<string, PutRequest>(row => row.Value, (row, request) => request.Value)
        .SetProperty(row => row.Version, row => row.Version + 1))
    .AllowWrite(row => row.Version)
    .ExpectVersion<int, PutRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
    .Returns(row => new EntryView(row.Id, row.Value, row.Version))
    .SimulateImmediateWrites();
```

Declare an eligible entity-shaped store or complete [public scalar read/write maps](INMEMORY-PROXY-WRITES.md),
and any required [insert/update factories](INMEMORY-GENERATED-VALUES.md), separately. All test features
remain off by default. `SimulateImmediateWrites(false)` removes only that command's opt-in;
`RequireTestOverride(reason)` always requires a script instead. Scripts still take precedence over
eligible generated backends; ResetTestDouble clears scripts/history, not fixture data or staging.

The conflict target must already satisfy production validation: supplied, non-null scalars of a
declared primary/alternate key or unfiltered unique index, including composite targets. A simulation
cannot bypass an invalid production writer/mapping, write permission, key or setter declaration.

## Branches and outcomes

| Saved conflict-target rows | InsertIfAbsent | InsertOrUpdate |
| --- | --- | --- |
| None | Inserted, one affected row | Inserted, one affected row |
| One | AlreadyExists, zero affected rows | Updated if approved predicates/tokens match; otherwise NotMatched |
| Multiple | Explicit fixture-arrangement error | Explicit fixture-arrangement error |

Successful commands return only the configured detached value, with `HasValue = true`. Existing and
NotMatched outcomes never return an existing/hidden row. Without `Returns`, the generated method
returns `CommandResult` rather than an invented entity/DTO. Unchanged matching updates still count one.

Conflict probes include **all saved rows**, even hidden ones. Designer visibility/bypasses, row guards
and expected versions constrain only the existing-row **update branch**, as in production; they do not
prevent insertion or make a hidden conflict disappear. Input validations run before either branch.
Conflict and explicit scalar/binary token comparison use ordinary CLR/structural equality, not SQL
collation or a configured fixture key comparer. Store key comparison remains a separate identity rule.

Updates retain the original saved primary/conflict keys regardless of the insert candidate's key.
Every setter reads the original saved row, not earlier setter assignments. A failed condition reports
NotMatched, without guessing absence versus optimistic conflict. The insert mapping is evaluated on
every call, including conflicts; insert factories/result mappings run only on successful insert branches.

## Values, defaults and pre-commit preparation

The insertion must supply every ordinary mapped scalar or declare a suitable explicit insert factory.
CLR property initializers, constructors and database defaults are not implicit native INSERT mappings.
Public-mapped candidates use generated scalar slots and never construct persistence entities.

Insert factories receive detached **saved same-store rows only**, excluding pending reservations.
Update factories run after approved setters and receive detached same-store candidates. Factories must
not change a conflict target; upsert setters/factories must not change the saved key. If no declared
conflict exists, a duplicate saved primary key throws rather than becoming AlreadyExists or an update.
This does not validate every SQL unique constraint, foreign key, CHECK, trigger or generated expression.

All candidate/copy/key/factory/public-encoder/result preparation and pre-commit cancellation complete
before saved rows change. Failure preserves earlier staging. Public insert encoders receive null saved
metadata; update encoders receive current detached metadata. Returned DTOs are detached.
This is not the tracked Save result boundary: tracked result mapping occurs after shared Save.
Production native writes inside an existing transaction may need caller rollback on failure; the
fixture's pre-commit rule does not promise identical intermediate SQL/transaction failure state.

## Staging, checkpoints and honest identity limits

No operation flushes, refreshes, discards or saves pending tracked entries. Selection ignores staged
creates/deletes/patches. A later shared Save can merge approved unrelated columns, detect stale tokens
or encounter a duplicate primary key when a native insert intersects a pending create reservation.
The pending entries remain available for explicit resolution. Atomic commands are not stageable.

Checkpoint rollback/disposal and baselines restore saved insert/update results, just like other
[immediate fixture writes](INMEMORY-IMMEDIATE-WRITES.md). Context-key partitions isolate their stores,
probes and factories. No ambient ownership, parallel SQL isolation or durable transaction is inferred.

A Max + 1 factory is **not SQL IDENTITY/AUTOINCREMENT**. Real database allocation can advance on
conflict/no-op attempts or rollbacks; fixtures do not reproduce that sequence. Exact shared fake/SQLite
contract tests deliberately supply immutable IDs. Separate tests check explicit seed-aware factories,
saved-only allocation, partition isolation and later pending-key collisions without claiming parity.

## Coverage and remaining boundaries

Explicit [atomic ReturnsSaved](../writes/ATOMIC-SAVED-RESULTS.md) can additionally read a fresh
DTO/relationship graph from a detached prepared root overlay and saved-only related stores.
It needs separate read/navigation permissions and never broadens graph mutations. This differs
from ordinary scalar `Returns`: root model/visibility policies also apply to the fresh result.

RGD3402 retains strict coverage for unsupported selected operations. Complete ordinary scalar stores,
maps, approved assignments and supported expressions are required. Graph reconciliation/cascades,
navigation-dependent mapping, owned/shared/inherited state, unconfigured EF filters/AutoInclude,
custom scalar conversions/comparers and arbitrary provider/entity behavior still require scripts or
[real-provider fixtures](REPOSITORY-CONTRACT-FIXTURES.md). Declaring factories does not widen that boundary.

Separately configured [atomic scalar capture](../writes/ATOMIC-SCALAR-CAPTURE.md) adds exact returned
shadow/field/indexer values with the matching production writer capability, complete mapped fixture
write state and per-command immediate consent. It does not enable non-ordinary identity/setter
targets, infer factories or broaden graph/provider semantics.

Portable scalar model filters now have a separate [per-command opt-in](INMEMORY-ATOMIC-FILTERS.md).
Conflicts/insertion remain unfiltered; upserts filter existing selection and recheck the model
filter on updated result reads. A filter-changing update fails rather than silently bypassing
production reload semantics. Filtered graph/provider shapes remain outside this subset.

Testing-only `atomicSimulation` receipts document the conflict target, update-only visibility,
branch outcomes, saved-only factory inputs, retained pending writes, pre-commit result construction,
checkpoint rollback and `relationalEquivalence: false`. SQL locks/races, monotonic identities,
constraints, cache/commit observers, retries and external effects are not simulated.
