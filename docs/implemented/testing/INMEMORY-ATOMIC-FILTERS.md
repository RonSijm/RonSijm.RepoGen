# Model-filtered insert-if-absent and upsert fixtures

Designed scalar `InsertIfAbsent` and `InsertOrUpdate` commands can use portable final-model
filters in generated fixtures. Each command requires both `SimulateImmediateWrites()` and
`UseInMemoryModelQueryFilter()`; entity read permissions do not enable either. Simple defaults
and existing unfiltered atomic outputs remain unchanged.

Start with [FilteredReservationRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryAtomicFilters/Configuration/FilteredReservationRepositoryDesigner.cs)
and its [designer → output map](../../../samples/ConfigurationExamples/MemoryAtomicFilters/README.md).
Production uses its existing explicitly selected atomic writer and EF queries. The fixture consumer
needs neither a DbContext nor that provider writer.

## Configure the complete operation

```csharp
entity.HasCommand("Put")
    .Create<SlotInput>(input => new Slot
    {
        Id = input.Id, Scope = input.Scope, Code = input.Code,
        Value = input.Value, Version = 0, Deleted = input.Deleted
    })
    .InsertOrUpdate(row => new { row.Scope, row.Code }, setters => setters
        .SetProperty<string, SlotInput>(row => row.Value, (row, input) => input.Value)
        .SetProperty(row => row.Version, row => row.Version + 1))
    .AllowWrite(row => row.Id).AllowWrite(row => row.Version)
    .ExpectVersion<int, SlotInput>(row => row.Version, (row, input) => input.ExpectedVersion)
    .Returns(row => new SlotView(row.Id, row.Value, row.Version))
    .SimulateImmediateWrites().UseInMemoryModelQueryFilter();
```

The example supplies its entire scalar insert mapping, unique conflict index, token and public
read/write fixture maps separately. `UseAtomicWrites<TWriter>()` remains a production capability
requirement. This fixture permission cannot bypass invalid mappings, keys, setters or writers.
Use either command setting with `false` to remove its consent; unsupported calls then need a script
or an explicit `RequireTestOverride(reason)` acknowledgement.

## Where the filter applies

| Branch | Model filter | Designer visibility / guards / expected versions |
| --- | --- | --- |
| Conflict probe | Not applied: all saved rows participate | Not applied |
| New insertion and its returned DTO | Not applied | Not applied |
| Existing-row update selection | Applied | Applied, with only explicitly named designer bypasses |
| Updated result read | Reapplied after setters/value factories | Not reapplied |

A filtered or hidden conflict is not a missing row. `InsertIfAbsent` returns `AlreadyExists`;
an upsert's filtered/nonmatching update returns `NotMatched`, without exposing a hidden result.
Inserting a row that will be hidden from later reads still returns `Inserted` and its configured DTO.
The existing-row key and conflict columns are retained, not replaced by the insert candidate's key.

The production upsert reloads by immutable conflict key through the model filter, independently
of the designer predicates. Fixtures preserve that distinction. An update can hide itself from
designer visibility and still return its changed DTO. An update that no longer passes the model
filter fails its updated result read with `InvalidOperationException`, even when no `Returns`
was configured. Production reload semantics are not changed to bypass the filter.

This differs from [native Update/Delete returned rows](INMEMORY-NATIVE-FILTERS.md), which are
matched post-update/pre-delete values, and from tracked [ReturnsSaved](../writes/SAVED-COMMAND-RESULTS.md),
whose result failure happens after a shared save.

## Failure, staging and transactions

Fixture setters, value factories, updated filter checks, encoders and result construction complete
before saved rows change. A false/throwing updated filter, mapping failure or cancellation leaves
saved and pending rows unchanged. Updated filter inputs are detached copies; they cannot mutate
the candidate through the filter delegate. Cancellation from a filter precedes result construction.

Real SQL already executes its update before the filtered reload. An operation-owned transaction
rolls back if that reload fails. Inside a caller-owned transaction, the update can remain pending
in that transaction until the caller explicitly rolls it back. The fixture's pre-commit failure
rule does not reproduce intermediate SQL transaction state. Tests must not infer equivalence
there, or repeat the failed mutation blindly.

Atomic commands do not flush, discard or refresh staged work. Later shared Save can detect an
old token or a primary-key collision with a previously staged create. Fixture checkpoints and
baselines restore saved state under their existing rules. Context-key partitions isolate probes,
results and staging; [explicit context-member bindings](INMEMORY-MODEL-FILTER-CONTEXTS.md) can supply
portable context filter inputs without evaluating captured context getters.

## Supported shape and receipts

The existing [atomic fixture boundary](INMEMORY-ATOMIC-WRITES.md) still applies: ordinary scalar
tables, complete public stores/maps, supplied or explicitly generated values, immutable declared
conflict targets and portable expressions. Scalar model filters use the same inspection as
[read filters](INMEMORY-MODEL-FILTERS.md). Helper translations additionally require per-command
`UseInMemoryTranslations()`; they never add production SQL client evaluation.

Unbound captures/services/clocks, navigation/shadow/inherited filter state, active AutoInclude,
filtered graph mutations, custom provider/conversion behavior and relational guarantees remain
script/real-provider boundaries. A missing filter or disabled fixture backend adds no filter
argument/receipt. Typed scripts always have priority; guards and cancellation precede automatic work.

Successful `modelQueryFilter`/`atomicSimulation` receipts distinguish unfiltered conflicts/inserts,
filtered update selection and model-only updated result reads. Strict unsupported commands retain
`RGD3402`. SQL locks/races, constraints, triggers, identity sequences and durability are not simulated.

Shared contract cases compare generated SQLite operations with entity-shaped and public-mapped
fixtures, including filter-changing failure with/without DTO results and explicit caller rollback.
