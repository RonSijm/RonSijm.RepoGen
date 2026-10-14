# Optional providers: registered queries, temporal history and native writes

Core RepoGen does not depend on SQL Server or assume one portable upsert dialect. Provider-specific
capabilities are explicitly selected; unsupported configurations fail rather than falling back to
an unsafe fetch-then-insert or silent client filtering.

## Registered parameterized query roots

An infrastructure class implements `IRepositoryQueryRoot<TContext, TRequest, TResult>` and returns
an `IQueryable<TResult>` using the already acquired context. The designer registers it:

```csharp
entity.HasQuery("FindNames")
    .FromProvider<SqlNamesQuery, SearchRequest, NameRow>(request => request)
    .OrderBy(row => row.Id).List();
```

Register the provider in DI. Generated operation constructors take the declared concrete providers;
application ports expose only typed requests/results. Subsequent result filtering, sorting,
grouping, dictionaries/lookups, paging and streaming remain composable.

Use EF parameterized `SqlQuery`/`FromSql` (or a provider's equivalent) inside the root. Interpolated
values must become parameters; identifiers/SQL stay infrastructure-owned. Runtime arbitrary SQL or
caller field/table interpolation is not a designer feature. The root must be queryable/composable
for later stages; an uncomposable stored procedure should remain a handwritten adapter behind an
application port, not be disguised as a composable SQL subquery.

The provider owns visibility, tracking, loading and tags. RepoGen rejects conflicting entity-root
clauses instead of silently discarding them. `DependsOn` is required for additional opaque cache
dependencies. The provider uses the same operation/work context and transaction; cancellation is
passed to materialization, including the cache's factory token. Root creation itself is synchronous
and must not do blocking I/O.

`RepositoryProviderAttribute` declares a required EF provider name. The final design-time context
must use that provider; a generic InMemory model cannot validate SQL Server/SQLite capabilities.

## SQL Server temporal history

The optional `RonSijm.RepoGen.SqlServer` package supplies
`TemporalQueryRoot<TContext, TRequest, TEntity>`. Configure its kind/time selectors in infrastructure
DI and reference that type through `FromProvider`. It supports All, AsOf, FromTo, Between and
ContainedIn. Input timestamps must have `DateTimeKind.Utc`; ranges require start before end.

```csharp
entity.HasQuery("History")
    .FromProvider<TemporalQueryRoot<MyContext, HistoryRequest, Entry>, HistoryRequest, Entry>(request => request)
    .Select(entry => new HistoryRow
    {
        Id = entry.Id,
        Name = entry.Name,
        ValidFrom = EF.Property<DateTime>(entry, "PeriodStart"),
        ValidTo = EF.Property<DateTime>(entry, "PeriodEnd")
    }).OrderBy(row => row.ValidFrom).List();
```

Period property names must match the EF model. History rows are no-tracking/read-only; an entity ID
alone does not uniquely identify all versions. Temporal joins/navigation shapes still depend on
the SQL Server provider. This is not a clinical audit system or an automatic history-write API.
SQL Server's temporal range boundary semantics are documented by
[EF's temporal-table documentation](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/temporal-tables).

The opt-in relational test uses `REPOGEN_SQLSERVER_TEST_CONNECTION` and creates only GUID-named
temporal/history fixture tables, then removes those same tables. It does not create/drop the supplied
database or modify existing tables. Actual local SQL Server tests have verified all five roots,
period DTOs, UTC validation and no tracking; normal test runs skip the database fixture without
that environment variable. SQLite tests are not used to claim temporal correctness.

## SQLite insert-if-absent and conditional upsert

Select the optional `RonSijm.RepoGen.Sqlite` writer explicitly:

```csharp
modelBuilder.Configure(options => options.UseClosedRepositories("Application.Contracts")
    .UseAtomicWrites<SqliteAtomicWriter>());

entity.HasCommand("Ensure")
    .Create<CreateRequest>(request => new Entry { Code = request.Code, Name = request.Name })
    .InsertIfAbsent(entry => entry.Code)
    .Returns(entry => new EntryResponse(entry.Id, entry.Name));
```

Register `SqliteAtomicWriter` in DI. The conflict target must be a declared unfiltered primary/unique
constraint over non-null supplied scalar columns; composite targets can use an anonymous key shape.
Identifiers come from EF metadata and are quoted; values use relational type mappings/parameters.

The implementation uses native `INSERT ... ON CONFLICT (...) DO NOTHING RETURNING ...`, inside one
operation transaction. On conflict, `InsertOrUpdate(key, setters => ...)` can perform a guarded SQL
update and reread the immutable key within that same transaction. Configure ownership/visibility
predicates and expected versions explicitly. Updates cannot change conflict columns. Outcomes are
`Inserted`, `Updated`, `AlreadyExists`, or `NotMatched`; the last does not guess missing vs stale.

The operation is immediate, not stageable. An existing work transaction is reused and its observers
wait for commit. Without one, the executor commits its own transaction before notifying observers.
Mapping failure before its own commit rolls the write back. It bypasses tracking and is not a
universal one-statement upsert/returning API.

Supported shape: one ordinary SQLite table with settable scalar CLR properties and EF relational
value conversion. Inheritance/multi-table/schema/shadow/computed-input/conflict-filter combinations
are rejected. Omitted insert properties need actual database defaults; CLR initializers are not an
implicit INSERT mapping. Returned generated keys/computed fields are read from SQLite RETURNING;
database trigger effects can have provider limitations. See
[SQLite UPSERT](https://www.sqlite.org/lang_UPSERT.html) and
[RETURNING](https://www.sqlite.org/lang_returning.html).

Race tests use independent SQLite file connections: eight same-key inserts yield one insert and
seven existing outcomes; two same-version conditional upserts yield one update and one NotMatched.
Owner guards, generated values, rollback/result failure and empty tracking are also executed.

## SQL Server atomic insertion and rowversion writes

The optional `RonSijm.RepoGen.SqlServer` package also supplies `SqlServerAtomicWriter`. Select
`UseAtomicWrites<SqlServerAtomicWriter>()` and register that writer in DI. The same designer
`Create(...).InsertIfAbsent(...)` / `InsertOrUpdate(...).Returns(...)` configuration works; no new
SQL Server dependency is added to core or to consumers that select SQLite/simple repositories.

The native insert uses parameterized `INSERT ... OUTPUT INSERTED ... SELECT ... WHERE NOT EXISTS`
with a `WITH (UPDLOCK, HOLDLOCK)` probe on the declared non-null, unfiltered primary/unique key.
The update/range locks protect the absence check until the operation/work transaction ends, and
the unique database constraint remains required. It does not use MERGE or swallow arbitrary
duplicate/constraint errors. The locking behavior is documented in
[SQL Server table hints](https://learn.microsoft.com/en-us/sql/t-sql/queries/hints-transact-sql-table).

Schema/column names are EF metadata, quoted by the provider; all supplied values are parameters.
Composite targets, computed/identity values, binary tokens, nullable scalars and EF scalar value
converters are supported. Returned rows are drained before reporting success: SQL Server can
deliver OUTPUT rows before an error. See [OUTPUT semantics](https://learn.microsoft.com/en-us/sql/t-sql/queries/output-clause-transact-sql).

Conditional upsert retains the existing explicit transaction strategy: first attempt the protected
insert, then execute the designer's guarded SQL update and reread the immutable key in that same
transaction. Outcomes and commit/cache observers are the same as SQLite. This is not a promise
of one-statement upsert. Native UPDATE/DELETE OUTPUT is a separate opt-in capability below.

For database-generated concurrency, configure `Property(x => x.Version).IsRowVersion()` on a
`byte[]` property and bind `ExpectVersion<byte[], Request>(x => x.Version, (x, request) => request.Version)`.
Do not map/set the database-generated token as an input assignment. A successful saved/atomic
DTO returns the current token. Tracked stale versions report Conflict; conditional set-based
upserts report NotMatched rather than guessing absence vs conflict. EF's original-token behavior
is documented in [optimistic concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).

The supported shape is one ordinary, unshared, non-inherited table with settable CLR scalars.
Identity/computed/rowversion input assignments, filtered/nullable conflict keys, shadow fields,
table sharing/splitting, declared triggers and memory-optimized tables are rejected. Schema names
are supported. `SqlServerAtomicWriter.ValidateModel(entityMetadata)` checks the table shape without
a connection, if an infrastructure runner wants early validation. The writer checks it again before
issuing SQL. Triggers unknown to EF and unsupported server/schema capabilities can still produce
database errors; direct OUTPUT cannot promise post-trigger values. Database defaults, not CLR
initializers, supply omitted columns.

Deadlocks, snapshot write conflicts and connection failures remain provider exceptions, not
optimistic outcomes. Use the explicit replayable execution-strategy runner when transient retries
are needed; it must replay the complete transaction with a fresh context. Stale client versions
are never retried into success. This strategy intentionally takes locks; test contention/isolation
with the actual application's database and key distribution.

The opt-in SQL Server fixture tests execute eight simultaneous composite-key inserts, competing
conditional rowversion upserts, tracked patches with both clients preloaded before either save,
null patches, one-time consume, explicit rollback/commit observers, cancellation, unrelated check
constraint failures, value conversion, computed values and quoted metadata/injection-shaped values.
They create/remove only GUID-owned tables in the supplied test database, not existing tables or
the database itself. Without `REPOGEN_SQLSERVER_TEST_CONNECTION`, relational SQL Server tests skip.

## Native bulk update/delete-and-return

Choose `UseReturningWrites<SqliteReturningWriter>()` or
`UseReturningWrites<SqlServerReturningWriter>()`, register that writer, and configure named
`Update(...).ReturnsMany(...)` / `Delete().ReturnsMany(...)` commands. They return an EF-free
`MutationResult<T>` containing every directly affected row's DTO, using one native DML statement
without preliminary entity fetches, shared `SaveChanges`, or tracker synchronization.

Mapping occurs before owned commit or inside a shared savepoint. Closed cache/work observers
follow the real commit, and unsupported provider/table/projection shapes fail rather than falling
back. Actual SQL Server tests exercise native OUTPUT, versioned races and savepoints, including
MARS rejection; SQLite tests verify native RETURNING and converted assignments.

See [Returning writes](../writes/RETURNING-WRITES.md) for exact configuration, locking, result semantics,
memory/tracker constraints and provider limitations.

## Returning deletes: explicit tracked alternative

For delete-and-return without a native returning dialect, a reviewed tracked `TrackedDelete()`
with an expected version and `Save().Returns(...)` is the explicit fallback. Independent-connection
consume races verify one successful returned row; losers report NotFound/Conflict. It loads the
row and saves, rather than claiming a portable single-statement DELETE RETURNING.

## Query/loading provider coverage

The optional `SqlServerQueryCoverageTests` exercise generated composite inner/left joins,
correlated existence/anti-joins, all four set operators, joined grouping/HAVING/counting/paging,
and dictionary column pruning on actual SQL Server. Loading fixtures compare request-filtered
nested single/split graphs, counted graph pages, global-filtered optional references and an
explicit historical bypass. Scalar DTOs prune unused graph includes. Owned streams check early
disposal and cancellation even when split-query rows are buffered. Unsupported custom predicates
propagate a translation error without client filtering and dispose the lease.

Those tests use `REPOGEN_SQLSERVER_TEST_CONNECTION`, transactional fixture initialization and
GUID-named tables, never database-level create/delete. They verify the documented fixture shapes,
not all providers or arbitrary LINQ/temporal/graph combinations. No SQL Server dependency is added
to the Design/core packages or ordinary SQLite examples. See [Navigation visibility](../queries/NAVIGATION-VISIBILITY.md).
