# Designer-defined result query pipelines

The designer can now configure clauses **after projection or grouping**, and expose the whole operation as one generated repository method. Application services do not need to assemble LINQ or selector chains.

This is available through the CLI/designer path, in both compatibility selector mode and opt-in closed-operation mode. It does not add a runtime expression language or change the attribute generator's API.

## Entity clauses versus result clauses

Existing `ProjectTo(...)`, `ProjectTo<T>()`, and `ProjectTo("name")` keep returning the entity query builder. This preserves their existing semantics: its `Where` and sort clauses still refer to the entity.

Use `Results<T>()` to explicitly switch to the configured DTO/group result. Named projection references are resolved before the generator validates that `T` matches. Missing/ambiguous references retain their existing diagnostics.

```csharp
entity.HasProjection(item => new ItemSummary
{
    Id = item.Id,
    Name = item.Name,
    Amount = item.Amount
});

entity.HasQuery("FindSummaries")
    .UseDefaultSort(false)
    .AsNoTracking()
    .ProjectTo<ItemSummary>()
    .Results<ItemSummary>()
    .Where<int>((summary, minimumAmount) => summary.Amount >= minimumAmount)
    .OrderBy(summary => summary.Name)
    .ThenBy(summary => summary.Id)
    .List();
```

The service calls `await repository.FindSummariesAsync(minimumAmount, cancellationToken)`.

`Select(expression)` is a convenient alternative that both projects and returns a typed result builder. Further `Select(...)` and `GroupBy(key, projection)` stages can change its row type. Anonymous **intermediate** rows are supported; public closed-operation results must still be named EF-free types or ordinary scalars/collections.

```csharp
entity.HasQuery("GetCategoryTotals")
    .UseDefaultSort(false)
    .Where<ReportRequest>((item, request) => item.Amount >= request.MinimumAmount)
    .Select(item => new { item.CategoryId, item.Amount })
    .GroupBy(item => item.CategoryId, group => new
    {
        CategoryId = group.Key,
        Count = group.Count(),
        Total = group.Sum(item => item.Amount)
    })
    .WhereIf<ReportRequest>(
        request => request.MinimumCount > 0,
        (summary, request) => summary.Count >= request.MinimumCount)
    .OrderByDescending(summary => summary.Total)
    .ThenBy(summary => summary.CategoryId)
    .Select(summary => new CategoryTotalDto(
        summary.CategoryId, summary.Count, summary.Total))
    .Paged();
```

With public application-owned `ReportRequest` and `CategoryTotalDto` types, closed mode emits a method shaped like:

```csharp
Task<PagedResult<CategoryTotalDto>> GetCategoryTotalsAsync(
    ReportRequest request,
    int page,
    int pageSize,
    CancellationToken cancellationToken = default);
```

Here `PagedResult` comes from `RonSijm.RepoGen.Abstractions` in closed mode. The existing two-argument entity `GroupBy(key, projection)` also supports `.Results<GroupDto>()` and subsequent clauses. Its terminal selection no longer silently discards grouping; for example, `.GroupBy(...).Count()` counts groups.

## SQL composition and ordering

Result stages preserve their configured order: `Where`, `Select`, `GroupBy`, `Distinct`, sorting, and `Skip`/`Take` remain an `IQueryable` pipeline until its terminal. Aggregate result predicates can translate to SQL `HAVING`; `Distinct().Count()` counts distinct results, not source entities. Grouped paging counts the groups left after their filters.

`OrderBy` starts a new ordering; `ThenBy` adds a key and must immediately follow a result `OrderBy`/`ThenBy` stage. Put other clauses before that sorting chain or start a new `OrderBy`. Invalid combinations fail during generation, rather than relying on casts that fail when constructing EF expressions. Reusing an earlier builder after a `Select`/`GroupBy` type transition is also rejected.

Translation remains provider-dependent. In particular, EF cannot compose SQL filters over every arbitrary DTO constructor, computed getter, or custom method. Prefer anonymous/member-initializer intermediate shapes, then construct the final DTO in the final `Select`. RepoGen does not rewrite constructor/business semantics or insert `AsEnumerable` as a fallback. An untranslatable filter fails normally through EF. EF may construct the final DTO client-side as part of its normal top-level projection.

`UseDefaultSort(false)` starts this named operation from `GetAllUnordered()` instead of `GetAll()`. Explicit configured sorts still apply. Its default is `true` for compatibility. Sorting a page requires a stable, unique tie-breaker chosen by the designer author; RepoGen does not infer one from an arbitrary DTO.

## Named terminal operations

Result builders expose `List`, `Single`, `SingleOrDefault`, `FirstOrDefault`, `Paged`, `Slice`, `Stream`, `Count`, `Exists`, `Dictionary`, `Min`, `Max`, `Sum`, and `Average`.

```csharp
entity.HasQuery("MaximumAmount")
    .Select(item => item.Amount)
    .Max(amount => amount);

entity.HasQuery("AmountsById")
    .UseDefaultSort(false)
    .Results<Item>()
    .Dictionary(item => item.Id, item => item.Amount);

entity.HasQuery("CategorySlice")
    .UseDefaultSort(false)
    .Select(item => item.CategoryId)
    .Distinct()
    .OrderBy(categoryId => categoryId)
    .Slice();
```

`Dictionary(key)` keeps the result row as its value; `Dictionary(key, value)` selects only the key/value pair before materialization. It returns `IReadOnlyDictionary<TKey, TValue>`. Duplicate keys throw by default; explicit `RepositoryDuplicateKeyBehavior.KeepFirst` / `KeepLast` overloads require guaranteed ordering. No implicit first/last-wins behavior is imposed. Null keys are invalid under every policy. The final dictionary assembly is in memory, after SQL projection, not a client-side filtering/grouping workaround. See [Dictionary results](DICTIONARY-RESULTS.md) for collision, limit, equality and cache semantics.

`Min`/`Max` use a typed selector; `Sum`/`Average` have the usual numeric and nullable numeric overloads. The generated return type follows the selected overload (`Average` over `int`, for example, returns `double`). Empty/null behavior follows EF/provider semantics: tested SQLite non-nullable `Sum` over an empty set returns zero, nullable `Min` returns null, and non-nullable `Max` throws. Optional single/first terminals return nullable values even for non-nullable scalar projections, so a missing row is not mistaken for zero.

`Slice()` returns `RonSijm.RepoGen.Abstractions.SliceResult<T>` with `Items`, `Page`, `PageSize`, `HasMore`, and nullable `NextOffset`. Pages are one-based; `NextOffset` is zero-based. It executes one bounded query for `pageSize + 1`, drops the extra row, and never requests a total count. Invalid page/size/count inputs and integer-overflowing limits/offsets fail before SQL executes.

`Paged()` ordinarily executes two queries: count, then items. Its default `BestEffort` consistency does not promise one statement or a consistent snapshot under concurrent writes. Explicitly use `Paged(RepositoryPageConsistency.Serializable)` for one stable read transaction spanning both queries. An existing Serializable/Snapshot context transaction is reused; a weaker or ambient transaction is rejected instead of silently weakening the configured guarantee. The owned read scope never saves/commits pending tracked writes. Serializable support/cost remain provider-owned; later page requests are not stabilized by one page's transaction.

`Stream()` retains the operation lease until enumeration completes, fails, is canceled, or is disposed early. Dispose an enumerator when stopping early.

Result builders now support `CursorPaged<TCursor>((row, cursor) => ..., row => ...)` on their **final** shape, after reviewed projection/grouping/ordering. The seek predicate is SQL-composed and one `pageSize + 1` query determines HasMore/NextCursor. Changing the result type afterward is rejected. Use translatable member-initializer DTOs or anonymous intermediate rows when continuing after a projection; arbitrary constructor member access is not generally translated by EF.

`CursorPaged<TCursor, TRequest>` can bind the request in the seek, cursor mapper, or both. Optional
`ValidateCursor` guards apply only to supplied continuation cursors and reject before acquisition/
cache lookup. Both features are designer-only declarations; unconfigured output stays unchanged.
See [Request-aware cursor paging](REQUEST-CURSORS.md) for naming, SQL, ordering and guard boundaries.

`RequirePagingOrder()` is an optional guard for offset pages/slices, and result cursors enable it automatically. Ordering must be declared after replacement roots/grouping/distinct. The guard detects missing ordering, not unique-key/tie-breaker correctness: configure deterministic keys (including ties) yourself. Entity cursors cannot be applied to a different result shape; use a result cursor there.

`Lookup(key, value)` is a named terminal returning `IReadOnlyDictionary<TKey, IReadOnlyList<TValue>>`, preserving duplicate keys' values. It selects only approved key/value columns in one SQL query and assembles the lookup after materialization; it is not a SQL aggregate or a client predicate fallback. Null keys are invalid, absent keys are absent, and value order follows declared query order. `Lookup(key)` keeps the whole result row as its value. This serializable shape also works with the optional closed cache adapter.

Dictionary/lookup key and value mappings also accept `(row, request)` expressions, independently
or together. Scalar `Min`/`Max`/`Sum`/`Average` selectors can use the same typed request. Paired
mappings align the value's request name to the key's, and terminal-only inputs participate in
nullability, EF-free contract checks, ports and cache keys. See [Request-bound terminals](REQUEST-TERMINALS.md).

## Reuse a request across clauses

Lambda parameters with the **same name and type** bind to one generated argument across source predicates, conditional predicates, source/result sorting, result projections, and result `Skip`/`Take`. Different names remain different inputs; using one name with conflicting types is an error. Do not capture design-time variables.

```csharp
entity.HasQuery("RequestedAmounts")
    .UseDefaultSort(false)
    .WhereIf<SearchRequest>(
        request => request.CategoryId != null,
        (item, request) => item.CategoryId == request.CategoryId)
    .Select<AmountRow, SearchRequest>((item, request) => new AmountRow
    {
        Id = item.Id,
        Amount = item.Amount + request.Adjustment
    })
    .Where<SearchRequest>((row, request) => row.Amount >= request.Minimum)
    .OrderBy<int, SearchRequest>((row, request) =>
        request.Descending ? -row.Amount : row.Amount)
    .ThenBy(row => row.Id)
    .Skip<SearchRequest>(request => request.Offset)
    .Take<SearchRequest>(request => request.Limit)
    .List();
```

The operation takes one `SearchRequest` and a cancellation token. `WhereIf` evaluates its input-only condition when the operation runs, and omits the SQL predicate entirely when false. It is not an authorization policy. Negative row limits are rejected, including parameter-driven limits. Generated infrastructure names and the `__repoGen` prefix are reserved.

## Boundaries and verification

The operations work through generated closed read ports, exact existing-port bindings, and legacy named extensions. Entity `ProjectionEnabled = false` continues to block runtime ad-hoc projection; it does not block reviewed designer pipelines. Closed result/request validation checks dictionary/lookup keys/values and scalar results without exposing intermediate EF entities in application contracts. Legacy cache branches compile for the new terminals; optional [closed scoped caching](../repositories/SCOPED-CACHING.md) coordinates them with context/work scope.

The JSON manifest now includes terminal kinds, the ordered result-stage inventory, conditional-stage flags, and the default-sort policy. This is an additive extension of schema version 1.

Tests compile and execute generated code on SQLite, inspecting emitted SQL for `HAVING`, projected `DISTINCT`, column-pruned dictionaries, grouped paging, bounded uncounted slices, anonymous/composite grouping, request-driven clauses, and absence of client-filter fallback. They also cover EF-free contracts, legacy restricted projections, empty/nullable aggregates, lease cleanup, streaming cancellation, invalid ordering, and stale builders.

`From(...)` now provides designer-defined typed sources, joins/subqueries/set operations, and
request-bound root expressions that continue through these result stages. See
[Multi-source queries](MULTI-SOURCE-QUERIES.md).

`HasFragment<TRequest>(name, (row, request) => predicate)` can be reused on reads/writes via `Use(fragment)`. `Validate<TRequest>(request => predicate, message)` rejects inputs before context acquisition. These are structural guards, not domain validation or authorization. `GroupBy<TKey,TResult,TRequest>(key, (group, request) => projection)` binds a request inside aggregates; the same named/typed input is shared across clauses. Request-bound includes/profiles are documented in [Loading profiles](LOADING-PROFILES.md). See [Implementation progress](../../other/IMPLEMENTATION-HISTORY.md) for delivered scope and remaining extensions.

Fragments now support immutable `And`/`Or`/`Not` composition, conditional `UseIf`, and explicit
request-name alignment. Result builders reuse them too: `modelBuilder.HasFragment<TRow, TRequest>`
and its parameterless-request overload support DTO/group/scalar rows without registering fake
entities. See [Reusable query fragments](QUERY-FRAGMENTS.md) for binding, SQL and policy boundaries.

Optional `UseCommandTimeout(seconds)` on the current result builder configures the whole named
closed query, including every command of a counted page and the lifetime of a stream. It overrides
entity/model defaults; explicit null retains the selected context setting and
`InheritCommandTimeout()` restores inheritance. No result stage or service parameter is added.
See [Command timeouts](../repositories/COMMAND-TIMEOUTS.md).

Optional result `OrderByIf`/`ThenByIf` (including descending variants), `SkipIf`/`TakeIf` and
`DistinctIf` omit whole stages on disabled branches. Request-bound keys/counts share the condition's
input name. Ordering guards require an unconditional fallback, and disabled limits are not
evaluated. See [Conditional query stages](CONDITIONAL-QUERY-STAGES.md) for SQL/paging semantics.

`OrderByField(request => request.SortBy, request => request.Descending, fields => ...)` chooses
only fixed, typed whitelist expressions over the current result row. It supports grouped and
projected aliases, explicit default/preserve-order behavior, adjacent ties and pre-acquisition
validation without adding a service-side query DSL. See [Result field sorting](RESULT-FIELD-SORTING.md).
