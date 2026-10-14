# Request-aware cursor paging

Cursor paging is an optional designer feature. Services still call one named method; they do not
build a query, choose a seek predicate, or map the next cursor themselves. Existing row-only
`CursorPaged<TCursor>` declarations and unconfigured repositories keep their existing output.
No new runtime service, package or CLI flag is needed.

## Configure the request, seek and cursor together

The [Reports designer](../../../samples/ConfigurationExamples/Reports/Configuration/ReportingRepositoryDesigner.cs)
defines this operation on its final DTO shape:

```csharp
entity.HasQuery("GetSalesCursor")
    .WhereIf<SalesCursorRequest>(request => request.Region != null,
        (sale, request) => sale.Region == request.Region)
    .Select(sale => new SaleCursorRow
    {
        Id = sale.Id,
        Region = sale.Region,
        Amount = sale.Amount
    })
    .OrderBy(sale => sale.Id)
    .OrderByDescendingIf((SalesCursorRequest request) => request.Descending,
        sale => sale.Id)
    .CursorPaged<SalesCursor, SalesCursorRequest>(
        (sale, cursor, request) => request.Descending
            ? sale.Id < cursor.Id
            : sale.Id > cursor.Id,
        (sale, formatting) => new SalesCursor(
            sale.Id, formatting.Descending, formatting.Region))
    .ValidateCursor<SalesCursor, SalesCursorRequest>(
        (cursor, request) => cursor.Descending == request.Descending
            && cursor.Region == request.Region,
        "This cursor belongs to a different region or direction.");
```

`SalesCursorRequest`, `SalesCursor` and `SaleCursorRow` are ordinary EF-free application types.
The service uses the generated repository or its bound handwritten port:

```csharp
var request = new SalesCursorRequest(Region: "North");
var first = await repository.GetSalesCursorAsync(
    request, new CursorPageRequest<SalesCursor>(20), cancellationToken);

if (first.HasMore)
{
    var next = await repository.GetSalesCursorAsync(request,
        new CursorPageRequest<SalesCursor>(first.NextCursor!, 20), cancellationToken);
}
```

Closed contracts use `RonSijm.RepoGen.Abstractions` page/cursor types. Compatibility and
CQRS-selector surfaces retain their generated infrastructure types and `First`/`After` factories.
The example checks ascending and descending continuation plus rejection of a different region.

## Independent request-aware overloads

Both the entity-root and final result builders accept these combinations:

| Seek predicate | Cursor mapping | Declaration |
| --- | --- | --- |
| `(row, cursor)` | `row` | Existing `CursorPaged<TCursor>` |
| `(row, cursor, request)` | `row` | `CursorPaged<TCursor, TRequest>` |
| `(row, cursor)` | `(row, request)` | `CursorPaged<TCursor, TRequest>` |
| `(row, cursor, request)` | `(row, request)` | `CursorPaged<TCursor, TRequest>` |

When both expressions take a request, the seek's third parameter is canonical. The mapper's
local request parameter is rebound to it, protecting nested lambda scopes. Other clauses,
context keys and guards share an argument when their request name and type match. Different
names remain independent inputs; a conflicting type for the same name is an error.

Inputs used only by a seek, mapper or guard are still discovered before nullability annotations
and API/data capture. They participate in closed EF-free contract validation, exact port binding,
cache keys and deterministic manifests. Explicit input-nullability annotations and design-time
type adapters work as they do on other query clauses. Nullable requests must be handled in
the configured expressions; an annotation does not insert runtime null behavior.

Entity-root cursors seek/map the entity independently of its configured DTO projection. Once
`Results()`, `Select()`, `From()` or a registered root replaces that surface, configure cursor
paging on the current result builder instead (`RGD2701`). Result cursors must use the final
result shape; reusing a stale builder or changing that shape afterward is rejected.

## Optional continuation guards

`ValidateCursor<TCursor>(cursor => ..., message)` requires no extra request.
`ValidateCursor<TCursor, TRequest>((cursor, request) => ..., message)` can compare continuation
metadata with a typed input. Multiple guards run in declaration order. They can be declared
before or after `CursorPaged`, but the final terminal must be cursor paging with the same cursor
type (`RGD2702`). Selecting another terminal leaves guards invalid instead of silently ignoring them.

Guards run only when `cursorPage.HasCursor` is true. First-page requests do not evaluate their
bodies. A failed guard throws `ArgumentException` with `ParamName == "cursorPage"` before context
acquisition, cache lookup or SQL. These checks run on cache hits too. Request-aware or guarded
cursors reject null page requests and already-canceled tokens at the same pre-acquisition boundary;
cancellation triggered by a successful guard is checked before proceeding.

Guard expressions are ordinary CLR structural checks, not SQL filters. They do not sign a cursor,
authenticate a client, enforce authorization, or prove that the continuation key existed. Declare
trusted tenant/visibility policies separately. Token serialization, signing, expiration and any
request fingerprint for filters omitted from your cursor are application-owned.

## Ordering, SQL and caching boundaries

New request-aware entity cursors and all result cursors require explicit ordering. Ordering must
survive replacement roots, grouping and distinct on every branch (`RGD1501`). The generator does
not synthesize an order, unique tie-breaker or matching seek predicate. Configure every key and
direction consistently; for a composite order, include all keys in the cursor and its lexicographic
seek. A guard compares only what you explicitly configure. Dynamic field sorting needs a matching
reviewed seek for every allowed field/direction; a whitelist alone cannot guarantee cursor correctness.

Each successful page executes one bounded `pageSize + 1` query, with no separate total count.
The seek remains SQL-composed, including supported group/HAVING shapes. EF/provider translation
failures are retained; no client filtering, sorting or paging fallback is inserted. Use translatable
member-initializer DTOs or anonymous intermediates before continued query composition. Requests
should be immutable/stable across guard evaluation, SQL construction and cursor mapping; these
overloads do not automatically snapshot every getter.

Entity cursors preserve their existing SQL pair projection for the item and cursor. Result cursors
preserve their existing final mapping of the last returned, buffered row. Result cursors may return
a last-row cursor even when `HasMore` is false; entity cursors retain their existing default cursor
on a final page. Consumers should stop based on `HasMore`, not merely the presence of a cursor.
Paging across separate calls is not a snapshot under concurrent writes, and this feature does not
add an implicit transaction.

Optional caching includes the request, page/cursor and configured operation shape/guards in its
identity. Guard validation precedes cache use; ordinary scope/visibility, pending-write bypass and
commit invalidation policies remain in force. Inspected cursor mappings also retain navigation
visibility review and inferred dependency behavior. No cache dependency is added when caching is off.

See [Result pipelines](RESULT-QUERY-PIPELINES.md), [Field sorting](RESULT-FIELD-SORTING.md),
[Scoped caching](../repositories/SCOPED-CACHING.md) and [Designer diagnostics](../generation/DESIGNER-DIAGNOSTICS.md).
