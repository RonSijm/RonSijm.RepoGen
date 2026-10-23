# Bounded fresh results after native updates

`Update(...).ReturnsSavedMany(projection, maximumRows: ...)` is an optional RepositoryDesigner mode.
It performs a set-based native update, captures the directly updated keys, then queries fresh
scalar/DTO/relationship results before the operation's owned commit or borrowed savepoint release.
Services call one named method; they do not compose queries or expose EF.

```csharp
modelBuilder.Configure(options => options.UseReturningWrites<SqliteReturningWriter>());

entity.HasCommand("Relabel")
    .Where<QueueRelabelRequest>((row, request) => row.QueueId == request.QueueId)
    .ExpectVersion<int, QueueRelabelRequest>(row => row.Revision, (row, request) => request.ExpectedRevision)
    .Update(setters => setters
        .SetProperty<string, QueueRelabelRequest>(row => row.Label, (row, request) => request.Prefix + row.Code)
        .SetProperty(row => row.Revision, row => row.Revision + 1))
    .AllowWrite(row => row.Revision)
    .ReturnsSavedMany(row => new QueueTicketReceipt(row.Id, row.Label, row.Revision,
        row.Queue == null ? "missing" : row.Queue.Name,
        row.Queue == null ? new List<string>() : row.Queue.Tickets
            .OrderBy(ticket => ticket.Code).Select(ticket => ticket.Label).ToList()), maximumRows: 20);
```

The result is the existing EF-free `MutationResult<TResult>`. `AffectedRows` equals the number of
directly updated roots and `Items` contains exactly one DTO for each root. Item order is unspecified;
sort a nested collection in the projection when it needs its own defined order.
`ReturnsSavedMany<TResult, TRequest>((row, request) => ..., maximumRows: ...)` also binds request data.

See the focused [QueueResultsRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryNativeResults/Configuration/QueueResultsRepositoryDesigner.cs)
and [MemoryNativeResults](../../../samples/ConfigurationExamples/MemoryNativeResults/README.md), which
links that designer to four exact outputs and an EF/model-free consumer.

## Execution and limits

| Phase | Semantics |
| --- | --- |
| Mutation | Existing native Update source filters, visibility/bypasses, approved setters and expected versions apply |
| Capture | Native RETURNING/OUTPUT identifies every directly updated row; there is no preliminary root fetch |
| Bound | More than maximumRows aborts and rolls back the operation; results are never silently truncated |
| Fresh query | Untracked query restricted by every saved primary-key part, not now-stale mutation/version predicates |
| Visibility | Root model filters and non-bypassed designer policies are rechecked; related policy remains explicit in the projection |
| Correlation | Complete structural keys match fresh results to changed roots; missing/duplicate rows fail the operation |
| Zero affected rows | Empty result; skips the fresh query, relationship snapshots and result constructors |
| Shared work | No SaveChanges, attach or tracker synchronization; pending roots/related edits are not flushed or substituted |

`maximumRows` is required and positive. It limits the permitted result set, not the SQL UPDATE with
TOP/LIMIT, and it does not cap the native scalar buffer before the writer finishes. Choose a bound
suitable for the graph size and provider's parameter/expression limits. Complete-key predicates use
the existing balanced-OR batch query infrastructure; there is no automatic chunking or client-side
filter fallback. The projection can still cause provider-specific split queries.

An explicit returning writer is required. Existing SQLite/SQL Server native metadata, table,
immutable-key, protected-assignment and translation restrictions remain. Primary keys must have
complete public-readable CLR properties. This does not enable graph mutations, shadow keys, new
assignment targets or a new SQL dialect. Optional shadow/mapped capture remains an independent
permission for root materialization; fresh result expressions execute as queries, not buffer reads.

Only native `Update` supports this mode. `Delete` keeps pre-delete scalar `ReturnsMany` results:
a deleted root cannot be freshly queried. Do not combine with Save, Stage, ReloadAfterSave,
WithOutcome, tracked batches or atomic InsertIfAbsent/InsertOrUpdate. Those commands have their
own [saved result](SAVED-COMMAND-RESULTS.md), [saved batch](SAVED-BATCH-RESULTS.md) and
[atomic saved result](ATOMIC-SAVED-RESULTS.md) lifecycles. The last result declaration wins;
choosing ordinary Returns/ReturnsMany clears the fresh-many mode and its bound.

## Transactions and failures

The existing [native returning executor](RETURNING-WRITES.md#transactions-cache-and-tracking)
owns a transaction when needed. Results are fully materialized and their actual cardinality checked
before commit; a custom result list's reported Count is not trusted. Bound, query, projection,
enumeration, cardinality or cancellation failure before
commit rolls it back. Within an explicit borrowed transaction, its operation savepoint rolls back
this update while preserving earlier successful operations. Borrowed transactions without savepoint
support and ambient transactions remain rejected before writing. This differs from the atomic
insert/upsert executor's caller-rollback policy.

Failures propagate normally, not as `RepositorySavedResultException`: the result is prepared before
the operation completes, unlike tracked ReturnsSaved after shared Save. A row that becomes hidden
causes failure and rollback rather than a shorter successful result. An explicitly surrounding
transaction still controls durability; result success does not mean that transaction has committed.
Cache/commit observers retain their existing success/real-commit boundaries.

This adds a separate fresh read and its cost. It is not native graph RETURNING, a one-round-trip
promise or an isolation/race guarantee. SQL translation, locks, parameter limits and provider
semantics need real-provider tests.

## Optional fixtures

Automatic fixture execution needs the existing store/command options, complete public write maps
where needed, and per-command `SimulateImmediateWrites`. Graph DTOs additionally need
`UseInMemoryTestQueries`, selected participating stores and every traversed HasInMemoryNavigation.
Scalar-only fresh results do not require automatic reads. Model-filter, mapped-state, relationship
and translation permissions remain independent; production output/contracts do not change when the
testing backend is disabled.

The fixture selects saved matching rows, applies the bound, prepares all approved updates/factories,
and fully encodes the root store once. It captures an independently detached **unpublished root
overlay** with saved-only related tables under one partition lock. All DTOs and inverse collections
see the complete new root state. Projection-owned related visibility and complete-key correlation
then run before publication.

Results are fully materialized and counted before publication. Bound, copy/encoder,
missing/duplicate result, DTO, enumeration, relationship and cancellation failures publish
nothing and retain earlier staging. Snapshot/committed roots are separate copies; adapters remain
responsible for nested mutable data. Callbacks cannot re-enter the fixture context. Scripts override
the backend, and keyed partitions, original-token conflicts, baselines/checkpoints keep their normal
semantics. SQL identities, constraints, locks, races, cascades and provider rowversions are not simulated.

The manifest adds `nativeSavedResultQuery`, the explicit maximum and abort/correlation/transaction
semantics. Covered fixture operations add a pre-publication `savedResultQuery` receipt. Unsupported
operations retain RGD3402/typed scripts even on empty stores. No preset or unconfigured output changes.

[Writes](README.md) · [Native returning](RETURNING-WRITES.md) ·
[Immediate fixtures](../testing/INMEMORY-IMMEDIATE-WRITES.md)
