# Fresh results from native atomic commands

`ReturnsSaved(...)` on `InsertIfAbsent` or `InsertOrUpdate` generates a fresh, untracked DTO query
inside the command's operation transaction. It runs only for an `Inserted` or `Updated` outcome,
before an owned commit. Services call a named method; the projection and relationship access stay
in the RepositoryDesigner.

```csharp
modelBuilder.Configure(options => options.UseAtomicWrites<SqliteAtomicWriter>());

entity.HasCommand("Put")
    .Create<EnrollmentChange>(request => new Enrollment
    {
        Id = request.Id, CourseId = request.CourseId, Code = request.Code,
        Name = request.Name, Version = 1
    })
    .AllowWrite(row => row.Id).AllowWrite(row => row.CourseId).AllowWrite(row => row.Version)
    .InsertOrUpdate(row => new { row.CourseId, row.Code }, setters => setters
        .SetProperty<string, EnrollmentChange>(row => row.Name, (row, request) => request.Name)
        .SetProperty(row => row.Version, row => row.Version + 1))
    .ExpectVersion<int, EnrollmentChange>(row => row.Version, (row, request) => request.ExpectedVersion)
    .ReturnsSaved(row => new EnrollmentReceipt(row.Id, row.Name, row.Version,
        row.Course == null ? "missing" : row.Course.Name,
        row.Course == null ? new List<string>() : row.Course.Enrollments
            .OrderBy(member => member.Code).Select(member => member.Name).ToList()));
```

The focused [EnrollmentRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryAtomicResults/Configuration/EnrollmentRepositoryDesigner.cs)
includes its model, fixture permissions and bound application port.
[MemoryAtomicResults](../../../samples/ConfigurationExamples/MemoryAtomicResults/README.md) maps the
designer to four exact outputs and an EF/model-free executable.

## Result and transaction boundaries

| Concern | Behavior |
| --- | --- |
| Insertion | Native inserted identity selects the fresh row, including generated keys |
| Update | Existing saved identity selects the result, not the insert candidate or stale version predicate |
| Identity | Every public-readable primary-key component participates in the fresh query |
| No match/conflict | `NotMatched`/`AlreadyExists` carry no existing-row result and skip the result query |
| Tracking | No tracking, attach or shared Save; pending root/related changes are not flushed or used as the result graph |
| Root visibility | EF model filters and designer policies apply; only explicitly bypassed designer policies are omitted |
| Related visibility | Explicit projection filters and existing navigation-policy review; no implicit designer-policy injection |
| Owned transaction | Native, translation, result, materialization or cancellation failure before commit rolls back |
| Borrowed transaction | No automatic operation savepoint; the owner must roll back on failure |
| Failure type | Original exception, not `RepositorySavedResultException` |

`ReturnsSaved<TResult, TRequest>((row, request) => ...)` can also bind explicit request values.
Atomic commands do not accept `Save`, `Stage` or `ReloadAfterSave`. Set-based Update/Delete remain
outside this result mode. Keys must have public readable CLR properties; capture permission does
not widen identity or assignment rules. Ordinary unique-target, provider/table and graph-write
restrictions remain in force.

This is a separate query with an additional cost, not a one-statement returned graph or a new upsert
dialect. The selected provider/isolation governs what concurrent changes the query can see. SQL
translation failures do not fall back to client-side querying. Normal command context ownership,
timeouts, scripts and cache/commit observers keep their existing boundaries.

`Returns(...)` retains direct scalar mapping from the native/updated result. Optional
[shadow/mapped scalar capture](ATOMIC-SCALAR-CAPTURE.md) can compose with `ReturnsSaved` when the root
needs that materialization capability. Its buffer supplies infrastructure identity; the fresh
projection still executes as a SQL query, not as rewritten buffer reads. The last result declaration
selects the mode. Defaults and unconfigured generated files do not change.

Unlike this atomic path, [tracked ReturnsSaved](SAVED-COMMAND-RESULTS.md) runs **after shared Save**
and reports a committed write through `RepositorySavedResultException` if its result fails.

## Optional fixtures

Automatic fixture execution requires `UseInMemoryTestStores`, `UseInMemoryTestCommands`, complete
ordinary public read/write maps where needed, and per-command `SimulateImmediateWrites`. Navigation
DTOs additionally require `UseInMemoryTestQueries` and each traversed `HasInMemoryNavigation`.
Existing strict expression, related model-filter and relationship-shape rules still apply.
Scalar-only fresh results do not require enabling automatic read methods.

The fixture prepares and fully encodes the root write once, then copies a detached snapshot of
that unpublished root store and the participating **saved-only** related stores under the same
partition lock. Relationship loading and the result see the inserted/updated root, even through
an inverse collection. They cannot see or flush staged changes. Result, adapter, encoding, graph
or cancellation failures publish no prepared rows and retain existing staging.

Snapshot objects and the committed store are separate copies. Callbacks run under the fixture's
non-reentrant guard and must not call back into its stores/context. Public encoder round-trip
validation and metadata preservation remain required. Generated-value factories, original tokens,
typed-script precedence, key partitions, baselines and checkpoints retain their existing behavior.
Capture, complete mapped write state, model-filter permission and immediate simulation are separate
choices; none implies the others.

Production `atomicSavedResultQuery` and fixture `savedResultQuery` receipts distinguish the
pre-commit result read from native scalar capture and post-shared-Save results. Unsupported fixture
shapes retain RGD3402/typed overrides. Fixtures do not emulate SQL identities, locks, uniqueness
races, rowversions or intermediate borrowed-transaction failure state; test those with a real provider.

[Writes](README.md) · [Atomic fixtures](../testing/INMEMORY-ATOMIC-WRITES.md) ·
[Provider capabilities](../repositories/PROVIDER-CAPABILITIES.md)
