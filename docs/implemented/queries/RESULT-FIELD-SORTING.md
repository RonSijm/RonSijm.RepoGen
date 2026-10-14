# Whitelisted field-name sorting on result pipelines

`OrderByField(...)` adds designer-controlled field-name sorting to entity, projected, grouped,
scalar, joined and registered-root results. It does not expose an expression language, reflection
mapper or EF query builder to application services. Existing entity `HasSortableFields(...)` and
named-query `AllowSorting(...)` remain unchanged; this is a separate, per-result-stage whitelist.
Nothing is generated unless the designer declares the stage.

```csharp
entity.HasQuery("RegionTotals")
    .Results<Sale>()
    .GroupBy(sale => sale.Region, group => new
    {
        Region = group.Key,
        Count = group.Count(),
        Total = group.Sum(sale => sale.Amount)
    })
    .OrderByDescending(total => total.Total)
    .OrderByField((ReportRequest request) => request.SortBy,
        preferences => preferences.Descending,
        fields => fields
            .Field("region", total => total.Region)
            .Field("count", total => total.Count)
            .Field("total", total => total.Total)
            .PreserveOrderWhenUnspecified())
    .ThenBy(total => total.Region)
    .Select(total => new RegionTotal(total.Region, total.Count, total.Total))
    .RequirePagingOrder()
    .Paged();
```

The application passes an ordinary request to one named operation:

```csharp
var page = await reports.RegionTotalsAsync(
    new ReportRequest(0) { SortBy = "count", Descending = true },
    page: 1, pageSize: 20, cancellationToken);
```

## Whitelist and input behavior

`Field<TKey>(alias, keySelector)` retains the selector's real CLR key type. Different fields can
use different types: integer aggregates, strings, nullable scalars and computed translatable keys
can share one whitelist. Aliases are matched with `StringComparison.OrdinalIgnoreCase`, independent
of the current culture. They need not match CLR property names. No trimming is performed.

All branches contain fixed, designer-authored `Queryable.OrderBy` / `OrderByDescending`
expressions. Request strings never become SQL identifiers or reflection property paths. Even an
explicitly configured alias containing quotes is only a comparison value, not SQL text.

| Unspecified field policy | Null or empty request field | Direction |
| --- | --- | --- |
| No additional option (default) | Reject with `ArgumentException` | Not evaluated |
| `UseDefault("count", descending: true)` | Sort by the declared default whitelist field | Use the literal configured direction; do not evaluate the request direction |
| `PreserveOrderWhenUnspecified()` | Omit this sort stage, preserving any incoming order | Not evaluated |

Unknown **non-empty** fields always fail, including whitespace unless it is part of an explicitly
declared non-blank alias. An invalid field never silently falls back to a different sort. A matched
field uses the request direction. Defaults can be registered before their field, but must refer to
a field in that same whitelist by the time configuration completes.

Default-field and preserve-order policies are mutually exclusive. Empty whitelists, blank or
duplicate case-insensitive aliases have `RGD2601` diagnostics. Missing/default-policy conflicts
have `RGD2602`. Null arguments, failing callbacks and superseded result builders do not append a
partially configured stage. The callback's builder is captured independently; retaining and later
mutating it does not alter the declared operation.

## Request preparation and ordering

The field selector provides the canonical request parameter. The direction selector is rebound
to that same input, even if its lambda uses another name. Use that canonical name and type in
other clauses to share one generated request argument. Typed request/key adapters are expanded
at design time; captured ordinary designer state is still rejected.

Field selectors are evaluated and resolved once per invocation, before context acquisition,
cache lookup and SQL execution. Request direction is evaluated once only for a matched field.
The internal field index/direction snapshot is reused across count/items and cache fills. It is
not exposed in generated public signatures. Membership preparation can participate in the same
operation. Closed-operation streams prepare inputs lazily when enumeration starts; selector-mode
named extension wrappers validate at invocation without acquiring operation-owned leases.
Cancellation and early disposal retain the existing owned/borrowed lease semantics.

The stage is a **primary sort**: a supplied field replaces prior ordering. Append `ThenBy(...)`
or `ThenByDescending(...)` afterward to supply deterministic ties. In the example, unspecified
sorting retains the total-based fallback, while a supplied field replaces it and the region key
breaks ties. Distinct and grouping discard incoming order guarantees, as elsewhere in the pipeline.

Strict/default-field sorts establish ordering on every successful branch. Preserve-order sorts
do so only if a fallback was already guaranteed. Conditional-only ordering cannot justify
adjacent `ThenBy`, guarded pages, or first/last dictionary selection. Those existing checks fail
with `RGD2401`, `RGD1501` or `RGD2502`; declare the fallback after grouping/distinct and before
optional field sorting. The guard checks ordering presence, not uniqueness; tie-breakers remain
designer-authored.

Cursor paging additionally needs a seek predicate matching the chosen field/direction. A fixed
cursor predicate must not be combined with arbitrary incompatible field choices. Keep its
whitelist/direction fixed, or explicitly align the request-aware query/seek design; the generator
does not synthesize a different cursor contract or seek predicate for every sort alias.

## SQL, cache and contract boundaries

Sorting, filtering, grouping, projection and paging remain provider-composed. Unselected field
expressions do not enter the SQL query; no reflection-based query construction or client sorting
fallback is emitted. A selected unsupported expression fails through EF and disposes the owned
lease. This is not a guarantee that arbitrary keys translate on every provider. Constructor DTO
member access should remain at the final projection when continuing over an anonymous or
member-initializer intermediate shape is required for translation.

Closed and compatibility cache identities include the whitelist/policies and resolved field/
direction. Results with equal serialized request data but different resolved sort choices do not
reuse the same entry. Original request arguments also remain in cache keys; case-insensitive
matching does not promise case-normalized cache identities. Use stable, immutable request DTOs:
ordinary cache serialization can itself read public getters independently of selector preparation.
All whitelisted navigation expressions participate conservatively in visibility review and cache
dependency inference, including fields not selected on a particular call. Related writes continue
to invalidate dependent cached reads after their commit.

Optional manifests record aliases, fixed key expressions, request bindings, fallback policy and
stage position under `resultFieldSorting`. Unconfigured model/output fingerprints remain unchanged.
EF-free application ports and API snapshots expose only the existing named method/request/result,
not the sorting builder or internal preparation inputs. Changing sort behavior alone is not an
API-signature change, and source API snapshots do not claim to detect every behavioral change.

The [Reports designer](../../../samples/ConfigurationExamples/Reports/Configuration/ReportingRepositoryDesigner.cs)
demonstrates inactive-by-default sorting by region/count/total using its existing application-owned
request. Its [program](../../../samples/ConfigurationExamples/Reports/Program.cs) checks grouped ascending/
descending results and rejects an unknown field before acquiring a context. Other examples remain
unconfigured and need no new runtime service, package or CLI argument.
