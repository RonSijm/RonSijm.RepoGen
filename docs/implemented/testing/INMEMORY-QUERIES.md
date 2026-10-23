# Optional portable in-memory repository reads

F15.3 adds an opt-in read backend to [shared fixtures](INMEMORY-FIXTURES.md). It lowers the same
RepositoryDesigner definitions used by production into typed LINQ-to-Objects delegates. Services
keep calling named application operations; there is no fake-specific selector/query DSL.

## Configure it in the designer

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Generated")
    .UseGeneratedNamespace("Persistence.Generated")
    .UseScriptedTestDoubles("Tests.Generated")
    .UseInMemoryTestStores()
    .UseInMemoryTestQueries());

modelBuilder.Entity<Note>(entity =>
{
    entity.WriteEnabled = false;
    entity.HasInMemoryStore(note => note.Id,
        note => new Note { Id = note.Id, Name = note.Name, Hidden = note.Hidden });
    entity.HasVisibilityPolicy("Visible", note => !note.Hidden);
    entity.HasProjection(note => new NoteSummary(note.Id, note.Name));
    entity.HasQuery("Find").Where<int>((note, id) => note.Id == id)
        .ProjectTo<NoteSummary>().SingleOrDefault();
});
```

The fixture's seeded NoteStore now supplies FindAsync automatically. Closed/bound overloads and
their exact argument/nullability shapes are retained. The separate testing output requires the
optional EF-free Testing runtime. Public row types can live in a contracts/data assembly so the
test consumer need not reference the EF/model assembly.

Both store generation and automatic reads are **off by default**. `UseInMemoryTestQueries(false)`
restores script-only behavior without removing stores. Presets retain explicitly selected testing
options. Entity `GenerateTestDouble(false)` excludes testing output and portability checks for that
entity. Public data-shape stores need an explicit `MapInMemoryEntity<TData>` scalar declaration
to run reads; storage adapters alone are not entity-expression or reverse DTO mappings.
See [public proxy read mappings](INMEMORY-PROXIES.md).

An optional [fixture-key comparer](INMEMORY-KEY-COMPARERS.md) changes arrangement/pending/saved key
identity only. Designer predicates, visibility, ordering, joins, dictionaries and navigation matching
retain their original LINQ semantics; fixture `Find(key)` is not a generated repository query.

## Capability matrix

| Designed shape | Automatic fixture read |
| --- | --- |
| Scalar predicates, ranges, supported string operations, conditional Where and reusable criteria/fragments | Yes, with .NET semantics |
| Request validation; WhereIn/WhereNotIn with null/empty/distinct/limit policy | Yes; membership values are snapshotted once before store access |
| Root visibility policies and explicit named bypasses | Yes; never inferred from property names |
| Default/explicit sorting, default-sort opt-out, root field whitelist and result field aliases | Yes; declared sort stages/branches are preserved |
| Named/custom projections, anonymous intermediate rows, result filters/maps/distinct/conditional Skip/Take | Yes |
| Grouped projections, grouped filters/order and regrouping | Yes |
| List, required/optional single, first, Count and Exists | Yes; missing non-nullable value projections become nullable, not zero |
| Min/Max/Sum/Average, dictionaries with Throw/KeepFirst/KeepLast, duplicate-preserving lookup | Yes, with .NET aggregate/equality/empty-input behavior |
| Counted pages, uncounted slices, entity or final-result cursors | Yes; paging inputs/overflow are checked and designer order/seek rules reused |
| Cold asynchronous streams | Yes; each enumeration obtains a fresh detached fixture snapshot on its first move |
| Typed context-key binding | Yes; designer UseContextKey selects Fixture.For(key) once per invocation |
| Tracking, cache hit/invalidation, SQL tags/timeouts/split strategy | Not simulated; reads are detached, uncached fixture execution |
| Active EF model filters | Optional [UseInMemoryModelQueryFilter](INMEMORY-MODEL-FILTERS.md) for portable row-only scalar read roots; otherwise RequiresOverride |
| Exact mapped scalar reads | Optional public-data [MapInMemoryProperty](INMEMORY-MAPPED-READS.md) bindings for shadow/field/indexer/CLR values; no entity getter, inferred write/token/relationship state |
| Context-captured filters, filtered navigation graphs, AutoInclude, inheritance/discriminators | RequiresOverride; no silent filter/model-policy loss |
| Typed `From` / `Set<TEntity>` replacement roots, inner/composite/left joins, correlated Any/anti-joins, cross joins and scalar set operations | Yes; participating entity-shaped or explicitly mapped public stores are captured together and each root's visibility applies before composition |
| Public fixture proxies for private/internal entities | Yes with explicit MapInMemoryEntity scalar assignments and complete per-operation member coverage; no persistence construction/identity/behavior adaptation |
| Declared reference/collection navigation and unfiltered Include/ThenInclude/load profiles | Yes with HasInMemoryNavigation and selected entity-shaped or explicitly mapped public stores; EF metadata supplies the keys, mapped sources use plain slots |
| Declared many-to-many navigation through an explicit CLR join store | Yes with selected endpoint/join stores and complete scalar keys; required FKs, optional source FKs and [explicitly non-null-filtered optional targets](INMEMORY-OPTIONAL-LINKS.md) share the [bounded loading rules](INMEMORY-MANY-TO-MANY.md) |
| Unmapped proxy navigation, filtered loading, shadow/custom key or collection behavior, implicit/shared joins or unguarded optional-target null propagation/loading, registered/raw/temporal roots | RequiresOverride; explicit joins/scalar maps do not imply arbitrary graph loading |
| EF/provider/custom scalar helper calls | RequiresOverride unless an exact [designer-declared fixture translation](INMEMORY-TRANSLATIONS.md) applies; no inferred provider/collation equivalence |
| Invoked delegates, ambient clocks/static state | RequiresOverride; translations cannot introduce these dependencies |
| Command-side tracked reads and provider-generated identities/rowversions | RequiresOverride; this read backend does not simulate tracked/provider state. Portable writes, Stage/Save/rollback and explicit scalar value factories are separately optional; see [write semantics](INMEMORY-WRITES.md) and [value policies](INMEMORY-GENERATED-VALUES.md) |
| Relational isolation guarantee for a page | RequiresOverride; a detached fixture snapshot is not a database transaction |

Known immutable framework constants such as string.Empty and Guid.Empty are portable. The
expression boundary includes ordinary strings/numerics/date arithmetic, Enumerable and selected
collection Contains operations; arbitrary application helpers are not assumed pure/portable.
The receipt is inspectable and conservative, not a SQL translation analyzer.

Optional [HasInMemoryTranslation](INMEMORY-TRANSLATIONS.md) supplies explicit scalar-call test
semantics across named read clauses without changing production expressions or contract/cache shapes.
It does not lower model filters, adapt arbitrary entity behavior or rewrite write/store/value adapters.
See [MemoryTranslations](../../../samples/ConfigurationExamples/MemoryTranslations/README.md).

## Multi-source queries use the same designer definitions

```csharp
entity.HasQuery("Joined")
    .From(sources => sources.Set<OrderEntry>().Join(sources.Set<DirectoryAccount>(),
        order => order.AccountId, account => account.Id,
        (order, account) => new OrderAccountRow
        {
            OrderId = order.Id, AccountName = account.Name, Quantity = order.Quantity
        }))
    .OrderBy(row => row.OrderId).List();
```

No second join builder, runtime query input or additional option is required. Production retains
its EF roots; the optional testing output rebinds typed Set roots to detached sequences and
whitelisted Queryable operators to matching Enumerable overloads. Designer expressions are
inspected/rendered, never compiled or invoked during generation. No EF or Queryable call appears
in the generated fixture implementation.

Every referenced root requires `HasInMemoryStore` with its actual public entity shape or an
explicitly mapped public data proxy on an enabled selected test-double entity. Store-only
participants need no operations of their own. Public proxies without MapInMemoryEntity remain
an override boundary; they do not reverse-map entity expressions.
A replacement root need not include the repository owner's table or store. Its owner's visibility,
default sort and includes do not leak into the source expression. Each participating root's actual
visibility policies apply before composition, including correlated/repeated roots and left joins;
only explicit designer bypasses remove them. Portable final-model scalar filters on each root may be
separately opted in with [UseInMemoryModelQueryFilter](INMEMORY-MODEL-FILTERS.md). Context-captured filters,
filtered navigation graphs, inheritance, AutoInclude and undeclared/non-portable navigation access on
**any** participating root still require overrides.
Explicit [HasInMemoryNavigation declarations](INMEMORY-NAVIGATION.md) can load suitable relationships
on the same coordinated snapshot before source composition; root policies are not injected into relationships.

Supported source operators are Where, Select, SelectMany, Join, GroupJoin, GroupBy, OrderBy/ThenBy
(both directions), Distinct, DefaultIfEmpty, Concat, Union, Except, Intersect, Any, All, Count,
LongCount, Contains, Sum, Min, Max, Average, Skip and Take. Conditional source branches are supported
when their query types can be rebound. Predicate/selector expressions retain the same conservative
portable boundary. Other operators or typed constructions that cannot be rebound receive explicit
coverage reasons, not a partially executed source.

The existing result pipeline and terminals continue after From: request guards, filters/maps,
grouped reports, dictionaries, scalar/nullable results, pages/slices/result cursors and cold streams.
Per-operation `sourceSnapshot` receipts list distinct roots and lowered operators. Strict checking
and typed scripted overrides work as for ordinary reads.

## Coverage, strict mode and overrides

The manifest's `testDoubles` section reports `PortableReadsWithScriptedOverrides`, per-operation
Generated/RequiresOverride status and deterministic reasons. It separately identifies .NET
execution, detached snapshots, unsimulated caching/tracking/relational behavior and reset policy.
An unsupported unarranged call throws UnsupportedInMemoryRepositoryOperationException with its
context/entity/signature and reason, even when the store is empty.

Opt into generation-time rejection of non-portable read operations:

```csharp
modelBuilder.Configure(options => options.RequirePortableTestQueries());

entity.HasQuery("History")
    .RequireTestOverride("Provider-specific history is covered by integration tests.")
    // Provider/root configuration remains the production source of truth.
    .Count();
```

`RequireTestOverride(reason)` is available before or after projection on the current builder. It
does not change the production query, suppress visibility or prove portability. Null removes the
acknowledgement and rechecks the shape; blank reasons fail. Strict mode rejects unacknowledged
non-portable **read** queries with RGD3302. Writes remain scripted regardless of this read setting.

Every generated read retains its typed script controls:

```csharp
testDouble.OnFindAsync.EnqueueException(new IOException("arranged failure"));
testDouble.OnFindAsync.EnqueueResult(new NoteSummary(99, "arranged"));
// Subsequent calls use the generated fixture read again.
```

Matching rules/queues take precedence, followed by an explicitly configured Runs/Returns/Throws
handler, then the generated backend. ResetTestDouble clears arrangements, counts and history,
retaining the backend, recording policy and fixture rows. ClearCalls changes no behavior.
Baseline/data reset remains a separate fixture action. Static coverage does not change because a
test installs an override; such calls are not proof that the generated backend implements that shape.

## Isolation, cancellation and meaningful limits

Each automatic read uses one store snapshot detached by the explicit copy adapter. LINQ executes
outside the fixture lock; predicates/projections must be pure. Counted pages materialize the
transformed results once so Items and TotalCount agree; slices use lookahead without a count.
Streaming holds the detached rows for that enumeration, so later seed/reset operations do not
change it. It is an in-memory snapshot, not low-memory database/network streaming.

Multi-source reads select one fixture partition and capture **all syntactically participating
stores together**, under that partition's context lock. Copies finish before query execution;
only selected stores are copied. Repeated references/self-joins reuse one snapshot for each table.
Conditional branches still capture every declared root, not just the selected branch. A failed
copy publishes no snapshot and changes no arranged rows/baselines. Streaming captures once per
enumeration, on its first move. This coordination prevents a mid-capture fixture mutation from
mixing source versions; it is not SQL isolation, cross-partition consistency or a write transaction.
No relationships are inferred between copied stores. Explicit HasInMemoryNavigation declarations
can rebuild the mapped-key relationships an operation actually accesses or loads.

Tokens are checked before validation/preparation/store acquisition, between row enumeration and
after materialization. A synchronous copy adapter, sort comparison or framework aggregate cannot
be interrupted midway; checks resume at the next boundary. Streams combine method/enumeration
tokens and retain normal scripted Completed/Disposed/Failed/Canceled recording behavior.
No artificial sleeps or background worker threads are introduced.

Arbitrary mutable request objects are not implicitly deep-cloned. Use immutable criteria and do
not mutate inputs while a read/stream executes. Recording copy adapters freeze *history*, not the
request passed to a handler. Detached stored graphs still depend on the declared copy adapter;
RepoGen does not repair shallow copies; only explicitly declared mapped-key relationships are loaded.

.NET null/equality/ordering/culture, numeric overflow and empty-aggregate behavior can differ
from a database provider's collation, conversions, null compensation, constraints and SQL. Keep
real-provider tests for translation, navigation visibility, concurrency, caching, isolation and
native commands. Shared behavioral cases exercise both generated fixtures and real SQLite
operations in the tooling tests; they do not establish SQL Server correctness or a universal parity guarantee.
The optional [contract-suite/provider harness](REPOSITORY-CONTRACT-FIXTURES.md) and
[RelationalContractFixture](../../../samples/ConfigurationExamples/RelationalContractFixture/README.md)
make that shared verification reusable in consumer tests.

## Examples and remaining work

- [MemoryCriteria](../../../samples/ConfigurationExamples/MemoryCriteria/README.md): designer criteria,
  named projections, visibility, sorting/pages, grouped totals, streams and automatic typed partitions.
- [MemoryCoverage](../../../samples/ConfigurationExamples/MemoryCoverage/README.md): strict receipts,
  acknowledged EF-filter/navigation operations and typed overrides, including an explicitly scripted write.
- [MemoryFixtures](../../../samples/ConfigurationExamples/MemoryFixtures/README.md) and
  [MemoryPartitions](../../../samples/ConfigurationExamples/MemoryPartitions/README.md): storage remains script-only.
- [MemoryJoins](../../../samples/ConfigurationExamples/MemoryJoins/README.md): composite/left joins,
  correlated anti-joins, unions, grouped pages and cold streams over two shared fixture stores.
- [MemoryProxies](../../../samples/ConfigurationExamples/MemoryProxies/README.md): an internal EF entity,
  public seed/result types, explicit scalar mappings, covered operations and deliberately unmapped fields.
- [MemoryNavigation](../../../samples/ConfigurationExamples/MemoryNavigation/README.md): declared composite
  relationships, nested DTOs, unfiltered profiles, optional references and explicit navigation visibility.
- [MemoryProxyNavigation](../../../samples/ConfigurationExamples/MemoryProxyNavigation/README.md): internal
  read-only/private-setter models, immutable public seeds, explicit scalar/relationship maps and nested
  DTOs, without persistence construction or an EF/model dependency in the consumer.

Declared public entity-shape navigation reads, explicit public scalar mapping and their
[mapped public-shape navigation combination](INMEMORY-PROXY-NAVIGATION.md) are delivered,
not arbitrary model-expression adaptation. Separately opt-in [tracked scalar fixture writes](INMEMORY-WRITES.md)
now support Stage/shared Save, explicit tokens and checkpoint rollback, including separately declared
[public scalar write adapters](INMEMORY-PROXY-WRITES.md). Suitable immediate scalar Update/Delete and
returning/consume commands can separately opt into [explicit fixture simulation](INMEMORY-IMMEDIATE-WRITES.md).
Suitable scalar insert/upsert definitions can also [explicitly opt into saved-state simulation](INMEMORY-ATOMIC-WRITES.md).
Additional generated-value/provider policies and graph reconciliation remain roadmap extensions.
[Explicit scalar generated-value factories](INMEMORY-GENERATED-VALUES.md) are supported by the separate
write backend. Suitable
[bounded root-row batches](INMEMORY-BATCHES.md) are supported by the separate write backend.
A separate optional relational harness
now executes the real implementation; it does not expand the fake's supported matrix.
Application-specific criteria helpers and business rules remain consumer-owned.
