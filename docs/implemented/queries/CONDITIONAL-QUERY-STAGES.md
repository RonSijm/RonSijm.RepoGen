# Optional query stages, configured in the designer

F07's request-dependent ranking can now omit entire sort expressions when disabled, rather than
always emit an SQL `CASE`. Optional limits and de-duplication use the same design-time pipeline.
No runtime query language, extra provider, CLI flag or service-side extension API is introduced.
Unconfigured queries and simple generation remain unchanged.

```csharp
entity.HasQuery("Find")
    .Select(row => new { row.Id, row.Name, row.Score })
    // Guaranteed fallback order when optional ranking is disabled.
    .OrderBy(row => row.Id)
    .OrderByDescendingIf((SearchRequest request) => request.Rank,
        (row, ranking) => row.Score + ranking.ScoreBias)
    .ThenByIf((SearchRequest request) => request.SortTiesByName,
        row => row.Name)
    .ThenBy(row => row.Id)
    .SkipIf(request => request.Offset.HasValue,
        (SearchRequest limits) => limits.Offset.GetValueOrDefault())
    .TakeIf(request => request.Limit.HasValue,
        (SearchRequest limits) => limits.Limit.GetValueOrDefault())
    .Select(row => new SearchRow(row.Id, row.Name))
    .RequirePagingOrder()
    .Paged();
```

The service still calls one named operation:

```csharp
var result = await repository.FindAsync(request, page, pageSize, cancellationToken);
```

Use these methods on the current `RepositoryResultQueryBuilder<TResult>`: after `Select(...)`,
`Results<T>()`, grouping, `From(...)` or `FromProvider(...)`. They work in closed-operation mode
and the existing named compatibility/CQRS-selector extensions. They do not add conditional
methods to every generated selector or change the attribute generator.

## Available clauses

| Method | Enabled branch | Disabled branch |
| --- | --- | --- |
| `OrderByIf` / `OrderByDescendingIf` | Replace the current primary ordering | Retain the existing query/order |
| `ThenByIf` / `ThenByDescendingIf` | Append a tie-breaker | Retain the existing order |
| `SkipIf` | Skip the configured count | Do not evaluate the count or add an offset |
| `TakeIf` | Take the configured count | Do not evaluate the count or add a limit |
| `DistinctIf` | De-duplicate the current projected row | Preserve duplicates |

Sort keys can be row-only or `(row, request) => key` expressions. Limits accept a non-negative
constant or `request => count`. Conditions are input-only boolean expressions; they are evaluated
in generated C#, not injected as optional SQL predicates. Limits evaluate their condition once
per stage and their count only when enabled. Use deterministic, side-effect-free conditions/counts;
queries, caches and counted-page execution are not a workflow engine.

Within each conditional clause, a request-bound key/count is rebound to the condition's request
parameter. The example's `ranking`/`limits` aliases therefore do not create extra service
arguments. Across other clauses, use the same request name/type when they represent one input.
Rebinding preserves nested lambda scopes and emits no expression invocation or compiled delegate.
Explicit [type adapters](../repositories/TYPE-ADAPTERS.md) expand inside conditions, keys and limits too.

For optional de-duplication:

```csharp
entity.HasQuery("Categories")
    .Select(row => row.Category)
    .DistinctIf<CategoryRequest>(request => request.UniqueOnly)
    .OrderBy(category => category)
    .List();
```

An optional clause changes only its query shape. It cannot turn off required visibility policies,
global filters, membership guards or context selection. No operator or field name is supplied by
the caller; all possible shapes remain declared in the designer.

## Ordering safety

`OrderByIf` replaces ordering when enabled; it does not prepend a key to the old sort list.
Configure an unconditional fallback first if the operation must always be ordered. Append the
desired tie-breakers after optional primary sorting. The generator does not infer uniqueness.

`ThenBy` and `ThenByIf` require **adjacent result ordering guaranteed on every branch**. This is
rejected:

```csharp
results.OrderByIf((SearchRequest request) => request.Rank, row => row.Score)
    .ThenBy(row => row.Id); // RGD2401: the disabled branch has no guaranteed result order.
```

Start with an unconditional result `OrderBy`, then optional order/tie stages. Two guarded clauses
are not treated as a proof just because their conditions look alike. `Where`, `Select`, limits and
other intervening result stages also break the adjacent `ThenBy` chain; start a new `OrderBy`
after them. An entity/root default sort is not an adjacent result-stage order.

For `RequirePagingOrder()` and result cursors, a conditional sort alone cannot satisfy the
existing guard. Grouping and `Distinct`/`DistinctIf` can discard order, so put an unconditional
`OrderBy` after them. `RGD1501` reports a missing guaranteed paging order. This is a conservative
design-time check, not proof that the chosen tie-breaker is unique or a query is cheap.

## Limits, paging and provider behavior

Negative constants fail at configuration time, even if their condition would be false. An enabled
negative request-derived count fails before SQL execution; its operation context/timeout scope
still cleans up normally. A disabled request-derived count or count helper is not read.
For nullable counts, use `HasValue` with `GetValueOrDefault()` as above; expression trees do not
retain C#'s null-forgiving `!` annotation for generated-code nullable analysis. Zero is valid;
`TakeIf(..., 0)` returns no rows when enabled.

Skip/Take stages apply **before the terminal**. A counted page's `TotalCount` describes the whole
configured pipeline, including an enabled skip/cap, not the unbounded source table. There is still
normally a count query plus an item query. Slices, dictionaries, lists and streams likewise see
the configured pipeline. Cursor seek expressions must match the configured ordering; optional
primary sorts do not automatically create new cursor semantics.

Active sort expressions still need to translate on the selected EF provider. An inactive
unsupported sort is absent from the query; an active unsupported sort fails through EF without
client-side sorting/filter fallback. Anonymous intermediate rows and member-initialized DTOs are
useful when composing further SQL operators. Keep constructor-based DTO mappings at the final
projection when the provider cannot translate member access through that constructor.

Existing caching keys include the operation shape and request values, so distinct enabled/disabled
requests do not share results. Inspectable navigation dependencies remain declared even when a
sort is disabled; invalidation is conservative. Existing visibility review, shared/owned/keyed
context, transaction, tracking and [timeout](../repositories/COMMAND-TIMEOUTS.md) policies remain in force.

## Example and output

The [Reports designer](../../../samples/ConfigurationExamples/Reports/Configuration/ReportingRepositoryDesigner.cs)
keeps total-based ordering as the default, optionally ranks grouped results by count, and optionally
skips/caps groups using its EF-free `ReportRequest`. The
[program](../../../samples/ConfigurationExamples/Reports/Program.cs) exercises default/ranked/limited/
skipped reports, checks context ownership and inspects all nine SQL-command timeouts. Service
method signatures stay the same; the three new request properties default to inactive.

Manifests use the existing stage `kind` and `conditional` inventory. No new manifest schema or
generated helper dependency is required. Null/stale arguments fail before mutating the design,
and generated contracts/API snapshots do not change merely because an existing request is reused
in a conditional stage.
