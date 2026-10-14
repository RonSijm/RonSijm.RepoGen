# Native update/delete-and-return

Returning writes are an optional designer capability for reporting changes, bulk maintenance and
consume-once operations. Services call a named method and receive application values; they do not
build EF queries or pass setter lambdas at runtime. Ordinary/simple generation is unchanged.

## Configure only the operations you want

Choose a provider explicitly, alongside closed repository contracts:

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Contracts")
    .UseReturningWrites<SqliteReturningWriter>());

modelBuilder.Entity<StockItem>(entity =>
{
    entity.HasCommand("AdjustStock")
        .Where<StockAdjustment>((item, request) => item.Category == request.Category)
        .Update(setters => setters.SetProperty<int, StockAdjustment>(
            item => item.Quantity, (item, request) => item.Quantity + request.Delta))
        .ReturnsMany(item => new StockChange(item.Id, item.Sku, item.Quantity));

    entity.HasCommand("RemoveEmpty").Where(item => item.Quantity == 0).Delete()
        .ReturnsMany(item => new StockChange(item.Id, item.Sku, item.Quantity));
});
```

Register `SqliteReturningWriter` in infrastructure DI. For SQL Server, choose and register
`SqlServerReturningWriter` from the optional SQL Server package instead. No SQL Server dependency
is added to SQLite/simple consumers. A custom infrastructure writer can implement
`IRepositoryReturningWriter`; its source query and assignment descriptors are trusted generated
inputs, not an application-facing arbitrary SQL API.

The generated write contract exposes:

```csharp
Task<MutationResult<StockChange>> AdjustStockAsync(
    StockAdjustment request, CancellationToken cancellationToken = default);
Task<MutationResult<StockChange>> RemoveEmptyAsync(
    CancellationToken cancellationToken = default);

// Service code:
var changes = await writes.AdjustStockAsync(request, cancellationToken);
```

`MutationResult<T>` is EF-free. `Items` contains a copied, read-only list of the directly affected
rows' projected values, and `AffectedRows` equals its count. Items have no defined order. An empty
result means no rows matched; it does not guess whether a row was absent, hidden, or stale.
The list container is immutable, not a promise that every DTO is deeply immutable.

`ReturnsMany<TResult, TRequest>((item, request) => ...)` can also bind request data into the result.
`ResultNullability<T>(...)` annotates each returned item, not the result wrapper. Existing
application-port binding and optional API/data snapshots include the new signatures and DTOs.

## What actually executes

Each operation sends one native DML statement: SQLite `UPDATE ... FROM ... RETURNING` /
`DELETE ... RETURNING`, or SQL Server `UPDATE` / `DELETE ... OUTPUT`. Transaction/savepoint control
uses additional commands; this is **not** a promise of one network round trip.

The source predicates and setter expressions are translated by EF. Query filters, required
designer visibility policies, and deliberately configured `ExpectVersion(...)` predicates remain
part of that source. Only an explicit `BypassVisibility(...)` bypasses a designer policy; it does
not disable EF global filters. EF-created typed parameters are retained; `ToQueryString()` debug
literals are not interpolated back into SQL. Table/column names come from quoted EF metadata, and
assignment aliases are generated and validated, not supplied by service callers.

Scalar value converters are applied to setter values and returned columns. Common duplicate setter
values and key-derived values share a single source alias. Setter expressions must translate fully;
EF's final-projection client-evaluation escape hatch must not silently write an untransformed input.
Conversion/provider expression shapes that cannot translate fail, rather than running client-side
row updates. SQL Server takes update locks on recognized EF target-table reads to protect version
decisions and computed assignments. Unsupported SQL root shapes fail closed; deadlocks, snapshot
conflicts and other provider failures are still exceptions, with no automatic retry.

Returned mapped scalar columns are buffered into fresh, **detached** row containers, then projected
into the configured application DTOs in memory. There is no preliminary entity fetch, no tracked
materialization, and no reread after the mutation. This currently returns all mapped scalar columns,
not a SQL-column-pruned DTO projection. The reader is drained before success, including SQL Server
errors that can arrive after OUTPUT rows. Batches must fit in memory; this is not a streaming import API.

SQLite requires RETURNING support (3.35+). Both providers return updated values after the direct
update, or pre-delete values for deletion. Cascades, trigger side effects, ordering and post-trigger
values are not implied. See [SQLite RETURNING](https://www.sqlite.org/lang_returning.html),
[SQLite UPDATE FROM](https://www.sqlite.org/lang_update.html), and
[SQL Server OUTPUT](https://learn.microsoft.com/en-us/sql/t-sql/queries/output-clause-transact-sql).

## Transactions, cache and tracking

- Without an existing context transaction, the executor owns a transaction and maps every result
  before committing. A provider/materialization/projection failure rolls it back.
- Inside an existing explicit transaction, it creates a savepoint. Failure rolls back this
  operation, retaining earlier successful operations. An existing transaction without savepoint
  support is rejected **before writing**; SQL Server MARS is one example. Ambient transactions
  are not accepted for these operations.
- The enclosing transaction still controls persistence. A returned result is not proof that the
  caller has committed that transaction.
- Shared-unit-of-work change tags are recorded only after successful mapping. Observers and the
  opt-in closed cache wait for the real commit; rollback/mapping failure does not invalidate it.
  Standalone committed writes notify using a non-cancelled token so late cancellation cannot
  simply skip invalidation. Observer failures remain committed-write failures, not retry signals.
- Native writes neither call `SaveChanges` nor synchronize the change tracker. Unrelated pending
  entities are not saved, and already tracked copies may be stale. Deliberately refresh/clear them
  in infrastructure before later tracked saves; do not blindly overwrite the returned version.

`ReturnsMany` is immediate and valid only with `Update` or `Delete`. Do not add `Save`, `Stage`,
`ReloadAfterSave`, `Create`, or `WithOutcome`. For stage-many/save-once, use existing tracked commands.
For a tracked single-row saved value, use `Returns`, not `ReturnsMany`. These remain different
mechanisms rather than hiding a fetch/save fallback behind native syntax.

## Supported mappings and deliberate restrictions

Supported: one ordinary unshared table, primary/composite keys, mapped scalar CLR properties with
getters/setters, a parameterless entity constructor, nullable assignments, EF scalar conversion,
computed returned values, and SQL Server schema names/binary rowversions. Expected versions must
be configured explicitly; application-managed version increments require an approved setter.

Unsupported: keyless/view/SQL-query roots, inherited/owned/shared/multi-table mappings,
shadow/backing-field-only columns, declared triggers, SQL Server memory-optimized tables, SQLite
schemas/virtual-table returning, key or generated-column assignments, and conditional tracked-only
patch setters. Keys remain immutable; protected foreign-key/token assignments still require
`AllowWrite` and must be allowed by the EF model.

Results may use mapped scalar values and scalar-argument helpers, not navigations or an opaque
whole-entity mapping helper. Native DML returns no related graph; configure a separate read when
one is needed. Unknown database triggers or unsupported server/schema/expression capabilities can
still raise provider errors; design-time metadata validation does not prove live SQL compatibility.

`UseReturningWrites<TWriter>(false)` disables the capability. It is off in all presets. Enabling an
unused capability adds no writer constructor/DI dependency to repositories without returning
commands. Commands still requesting `ReturnsMany` after disabling it fail with a diagnostic.

## Runnable example and verification

The [Inventory designer](../../../samples/ConfigurationExamples/Inventory/Configuration/InventoryRepositoryDesigner.cs)
and [Inventory application](../../../samples/ConfigurationExamples/Inventory/Program.cs) demonstrate two
bulk updates/deletes, an application-owned write port, detached results, and plain direct-context
DI with no tenant resolver, cache or unit-of-work requirement.

SQLite and actual opt-in SQL Server tests cover multi-row returns, composite keys, quoted names,
injection-shaped values, conversions, computed values, global/designer filters, expected-version
races, cancellation, empty results, rollback/savepoints, MARS rejection, shared commit observers,
and cache invalidation. SQL Server fixtures use `REPOGEN_SQLSERVER_TEST_CONNECTION`, own only
GUID-named fixture tables, and never create/drop the supplied database or alter existing tables.
