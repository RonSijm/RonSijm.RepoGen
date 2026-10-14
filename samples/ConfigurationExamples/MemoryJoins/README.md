# MemoryJoins: designed queries over shared fixture tables

Start with [OrderReportsRepositoryDesigner](Configuration/OrderReportsRepositoryDesigner.cs).
It generates the same seven application-port operations for production EF and an optional
EF-free fixture. The two tables have no navigation relationship: matching is explicitly designed,
not inferred from foreign-key-looking property names.

## Designer → generated output

The [generation runner](../GenerationRunner/Program.cs) pairs this designer with
[OrderReportsContext](Model/OrderReportsContext.cs) and writes these four artifacts:

| Designer declaration | Exact output |
| --- | --- |
| `BindReadPort<IOrderReports>()` | [Closed interfaces](../Contracts/Generated/OrderReportsRepositories.g.cs): `IReadOrderEntryRepository : IOrderReports` |
| Both `HasInMemoryStore(...)` declarations | [TestDoubles.g.cs](Generated/TestDoubles.g.cs): `OrderReportsContextRepositoryFixture.Data.OrderEntryStore` and `DirectoryAccountStore`, shared by both generated doubles |
| `Joined` / `LeftJoined` | `JoinedAsync()` / `LeftJoinedAsync()` on `OrderEntryRepositoryTestDouble`; composite key join versus `GroupJoin` + `DefaultIfEmpty`, including null account results |
| `Unmatched` | `UnmatchedAsync()`: correlated anti-join against the account source |
| `Totals` | `TotalsAsync(criteria, page, pageSize)`: validation, source request filter, join, grouped Count/Sum, result filter, ordering and counted page |
| `Directory` / `Identifiers` | Account-only replacement root and ID union; owner default clauses do not leak into a replacement root |
| `Stream` | `StreamAsync()`: cold joined stream, with both stores copied together on first enumeration |
| The same queries | [Production operations](Persistence/Repositories.g.cs): `OrderEntryRepositoryOperations` with EF/Queryable execution; not compiled into this fake-only executable |
| Strict automatic-read selection | [manifest.json](Generated/manifest.json): Generated coverage and each operation's participating roots/operators/atomic snapshot receipt |

Every source must have an enabled entity-shaped store. `DirectoryAccount` has a store and a
visibility policy but needs no query operations of its own. Each root's visibility is applied
before composition, so a hidden account remains absent even when an order is visible. Left joins
retain the visible order with a null account; they do not resurrect hidden accounts.

## Consumer and boundaries

[Program.cs](Program.cs) arranges isolated data, obtains [IOrderReports](Contracts/IOrderReports.cs)
from DI and calls the named operations. The designer owns all matching/filtering/reporting logic.
The consumer checks joins, unmatched rows, grouped pages, unions, shared DI identity and cold
streams. It also clears the account store mid-enumeration to show that the current stream keeps
its detached snapshot, then restores the baseline for both stores.

This project references Contracts, the optional Testing runtime and DI only. No EF, model,
designer or generation engine is a runtime dependency. Public row shapes are compiled in
Contracts; the model and designer are linked into separate design-time projects.

```powershell
dotnet run --project samples/ConfigurationExamples/MemoryJoins -c Release
```

Running the consumer does not regenerate files. After changing inputs, run
`./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Run` from the repository root.

The generated fake uses LINQ-to-Objects, not SQL semantics. Its snapshot coordinates participating
stores in one partition, not database isolation or cross-partition transactions. Includes,
navigation fixup, EF model filters, provider-only roots, public proxy-expression adaptation and
stateful writes still need explicit overrides. The tooling tests compare shared source cases
with actual generated SQLite operations; that is not a SQL Server parity guarantee.

See [portable reads](../../../docs/implemented/testing/INMEMORY-QUERIES.md),
[shared stores](../../../docs/implemented/testing/INMEMORY-FIXTURES.md) and
[real-provider contract fixtures](../../../docs/implemented/testing/REPOSITORY-CONTRACT-FIXTURES.md).
