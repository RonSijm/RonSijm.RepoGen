# Reports: designer-defined reporting queries

EF-free read ports, grouped pages, typed request terminals and guarded cursor paging.

## Start here

1. [ReportingRepositoryDesigner.cs](Configuration/ReportingRepositoryDesigner.cs) — what RepoGen generates.
2. [ReportingContext.cs](Model/ReportingContext.cs) — the EF context and entity model.
3. [ReportContracts.cs](Contracts/ReportContracts.cs) — handwritten application types and ports, not generated code.
4. [Program.cs](Program.cs) — setup, repository usage and executable assertions.
5. [Generated](Generated) — checked-in generator output; do not edit it manually.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs `ReportingContext` with this designer:

```csharp
Generate(reporting, new ReportingRepositoryDesigner().CreateModel(), "Reports",
    contracts: "Contracts/Generated/ReportingRepositories.g.cs");
```

| Generated file | What to find in it |
| --- | --- |
| [Contracts/Generated/ReportingRepositories.g.cs](../Contracts/Generated/ReportingRepositories.g.cs) | `IReadSaleRepository : ISalesReports` and combined read-only `ISaleRepository` |
| [Generated/Repositories.g.cs](Generated/Repositories.g.cs) | Internal `SaleRepositoryOperations`, configured query bodies, context-lease handling and DI registration |
| [Generated/manifest.json](Generated/manifest.json) | Grouped/result stages, cursor guards, timeouts, source sets and bound read port |

| Declaration/input | Specific generated effect |
| --- | --- |
| `UseClosedRepositories(...)` / `WriteEnabled = false` / `BindReadPort<ISalesReports>()` | EF-free read interface inherits the [handwritten ISalesReports port](Contracts/ReportContracts.cs); no `IWriteSaleRepository` |
| `HasQuery("GetRegionTotals")...GroupBy(...).Select(...RegionTotal).Paged()` | `GetRegionTotalsAsync(ReportRequest, page, pageSize, ...)` returns `PagedResult<RegionTotal>` with group count/sum, declared filters and sorting inside its implementation |
| `.OrderByField(...fields.Field("region", ...).Field("count", ...).Field("total", ...))` | The same method accepts sorting through `ReportRequest`; generated code validates those three aliases instead of accepting arbitrary SQL fields |
| `HasQuery("GetTotalsByRegion")...Dictionary(...)` | `GetTotalsByRegionAsync(ReportAmountRequest, ...)` returns `IReadOnlyDictionary<string, int>` with request-bound keys/values |
| `HasQuery("GetScaledAmount")...Sum<ReportAmountRequest>(...)` | `GetScaledAmountAsync(ReportAmountRequest, ...)` returns `int` with the declared scaling and optional region filter |
| `HasQuery("GetSalesCursor")...CursorPaged<...>().ValidateCursor<...>()` | `GetSalesCursorAsync(SalesCursorRequest, CursorPageRequest<SalesCursor>, ...)` returns `CursorPagedResult<SaleCursorRow, SalesCursor>`; generated guards reject mismatched region/direction |
| `HasQuery("GetSalesWithLabels").From(...left join...).List()` | `GetSalesWithLabelsAsync(...)` returns `IReadOnlyList<SaleDetails>` without a `RegionLabel` repository |
| `UseAsyncContextLeases()` / `UseCommandTimeout(...)` | Internal implementations acquire/dispose owned contexts and apply timeouts; neither becomes a service-side query method |

The public generated contract is short because it **inherits** all five method signatures from
`ISalesReports`. To see their generated behavior, open `Repositories.g.cs` and search for
**`SaleRepositoryOperations`** or **`GetRegionTotalsAsyncCore`**. Query bodies are merged into that
file here; there is no separate `Queries.g.cs` or API snapshot for this example.

## Run

From the RepoGen repository root, using the checked-in generated code:

```powershell
dotnet run --project samples/ConfigurationExamples/Reports -c Release
```

After changing inputs, regenerate with `./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`
from the repository root. Running `Program.cs` does not run the designer or regenerate repositories.

## What this example demonstrates

[ReportingRepositoryDesigner](Configuration/ReportingRepositoryDesigner.cs) opts into a separate,
EF-free contract namespace, configured-entity allowlisting, and async context leases. It sets
`WriteEnabled = false` and binds the existing application-owned `ISalesReports` interface.
Closed named reads default to no-tracking, independently of write tracking. The application
contract exposes no tracking methods. See [Tracking policies](../../../docs/implemented/repositories/TRACKING-POLICIES.md).

`GetRegionTotals` groups sales by region, calculates count and sum, conditionally filters the
groups using `ReportRequest.MinimumCount`, sorts deterministically, projects to `RegionTotal`,
and returns a counted page. All of this is one configured operation.

Optional [conditional stages](../../../docs/implemented/queries/CONDITIONAL-QUERY-STAGES.md) reuse that same method and
request: `RankByCount` selects declared count-based ordering, while nullable `SkipGroups` and
`LimitGroups` opt into group skipping/capping. All three default to inactive. Disabled limits do
not read their nullable values, and the total count describes the configured capped/skipped
pipeline. No service-side SQL sort or limit extensions are exposed.

Optional [result field sorting](../../../docs/implemented/queries/RESULT-FIELD-SORTING.md) uses `SortBy`/`Descending` on
the same request and method. The designer fixes the `region`, `count` and `total` aliases. Null/
empty `SortBy` preserves the earlier total/ranking order; a supplied field replaces it, with region
as a tie-breaker. Unknown fields fail before context acquisition. The program checks ascending
count and descending total sorting plus rejected SQL-looking input; no field string becomes SQL.

Optional [request-bound terminals](../../../docs/implemented/queries/REQUEST-TERMINALS.md) use `ReportAmountRequest` in
`GetTotalsByRegion`'s grouped dictionary labels/values and `GetScaledAmount`'s sum mapping. The
paired dictionary lambdas share one canonical input despite different local names. Optional
region filtering and the sum use that same request; services do not project or aggregate rows.
A negative multiplier fails before acquiring a context.

Optional [request-aware cursors](../../../docs/implemented/queries/REQUEST-CURSORS.md) configure `GetSalesCursor` with
`SalesCursorRequest`, reviewed ascending/descending ID ordering, a matching seek and cursor metadata.
The generated named method accepts an EF-free `CursorPageRequest<SalesCursor>`; the service neither
builds a predicate nor maps a cursor. The program checks both directions and rejects a cursor from
another region before acquisition. The guard is structural validation, not token signing or authorization.

`GetSalesWithLabels` uses `From(sources => ...)` to left-join the separate `RegionLabel` table.
It preserves sales without a label, returns a nullable caption, and neither requires a navigation
relationship nor exposes a second repository. There is no extra CLI option for this feature.

Optional [designer timeouts](../../../docs/implemented/repositories/COMMAND-TIMEOUTS.md) demonstrate model/entity/operation
inheritance without changing these service signatures. The grouped page overrides the eight-second
model and five-second entity defaults with three seconds for each SQL command. The left join uses
`UseCommandTimeout(null)` to retain the context's seventeen-second provider setting. The program
checks all twenty read commands, including the three terminal and four cursor reads inheriting the entity's
five-second default; the other examples acquire no additional policy by default.

The service uses only:

```csharp
var page = await repository.GetRegionTotalsAsync(new ReportRequest(2), 1, 10);
var totals = await repository.GetTotalsByRegionAsync(new ReportAmountRequest(2, " branch"));
var amount = await repository.GetScaledAmountAsync(new ReportAmountRequest(3, Region: "North"));
var sales = await repository.GetSalesCursorAsync(
    new SalesCursorRequest(Region: "North"), new CursorPageRequest<SalesCursor>(20));
var details = await repository.GetSalesWithLabelsAsync();
```

The [Contracts project](../Contracts/Contracts.csproj) references result abstractions but no EF or
entity project. Its [application port](Contracts/ReportContracts.cs) owns the method signature.
The generated [read repository interface](../Contracts/Generated/ReportingRepositories.g.cs)
inherits that port, so it does not redeclare the same method. No `IWriteSaleRepository`, public
selector, `DbContext`, or generic write API is exposed there.

[Program](Program.cs) registers the async factory/lease adapter and generated DI helper,
verifies the bound interface and generated interface share one implementation, and checks that
each read creates/disposes exactly one owned context. The sample factory deliberately rejects
synchronous creation. These registrations are needed because this example explicitly enables
leases; they are not requirements for either of the other examples.

Inspect its generated [implementation](Generated/Repositories.g.cs) and
[manifest](Generated/manifest.json). The shared externally owned SQLite connection only
keeps the in-memory demo database alive; disposing an operation context does not own that connection.

## Why these files are not compiled into the consumer

The files are kept together for reading, but the shared [Designers](../Designers/Designers.csproj),
[Models](../Models/Models.csproj) and [Contracts](../Contracts/Contracts.csproj) projects compile their
respective inputs. [ExampleSources.props](../ExampleSources.props) excludes those inputs from this
consumer's compilation. This preserves the design-time bootstrap and EF-free contract boundary;
the consumer does not reference the Designers project.

[All examples and suggested reading order](../README.md).
