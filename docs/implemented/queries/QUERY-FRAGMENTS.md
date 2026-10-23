# Reusable designer query fragments

Fragments keep approved query decisions in the designer. They can now be composed with `And`,
`Or` and `Not`, conditionally applied with `UseIf`, and reused after projection or grouping.
The generated application port still exposes a complete named method, not a predicate builder.

This is entirely opt-in. An unused model-level fragment changes no generated output. No new CLI
switch, runtime helper, mapper, cache, context resolver or service dependency is required. It works
with closed operations, CQRS selectors and compatibility named extensions. It does not change the
attribute generator's runtime API.

## Compose approved entity predicates

```csharp
public sealed record SearchRequest(int Minimum, int Maximum, string? Term, bool LimitAmount);

var entity = modelBuilder.Entity<Item>();
var minimum = entity.HasFragment<SearchRequest>("Minimum",
    (item, bounds) => item.Amount >= bounds.Minimum);
var maximum = entity.HasFragment<SearchRequest>("Maximum",
    (other, limits) => other.Amount <= limits.Maximum);
var name = entity.HasFragment<SearchRequest>("Name",
    (item, text) => item.Name.Contains(text.Term!));
var category = entity.HasFragment<SearchRequest>("Category",
    (item, selection) => item.Category.Contains(selection.Term!));
var blank = entity.HasFragment("BlankName", item => item.Name.Length == 0);

entity.HasQuery("Search")
    .Validate<SearchRequest>(request => request != null && request.Minimum >= 0
        && request.Maximum >= request.Minimum, "Invalid amount bounds.")
    .Use(blank.Not())
    .UseIf<SearchRequest>(request => request.LimitAmount, minimum.And(maximum))
    .UseIf<SearchRequest>(request => request.Term != null && request.Term.Length > 0,
        name.Or(category))
    .OrderBy(item => item.Id)
    .Select(item => new ItemSummary(item.Id, item.Name))
    .List();
```

The service calls `repository.SearchAsync(request, cancellationToken)`. Operators, fields and
grouping are fixed at design time; request values are normal provider parameters. Parentheses
preserve the composition tree: `a.Or(b).And(c.Not())` means `(a OR b) AND NOT c`. Providers may
optimize the resulting SQL, for example replacing equality OR clauses with `IN`.

Composition returns a new handle; it does not modify either original fragment. The composed handle
retains the left fragment's `Name`, which is a design label, not a generated operation name.
Parameterless fragments and fragments for the same request CLR type can be mixed in either order.
Distinct request types cannot be combined into one fragment; use separate approved clauses instead.

## Request names and identity

Fragment lambdas can use different local names (`bounds`, `limits`, `text`). Composition rebinds
their row/request parameter identities and inlines the bodies. It introduces neither
`Expression.Invoke` nor compiled delegates into the generated query. Nested lambda bindings are
preserved; generated local-name protection still applies.

`And`/`Or` choose the first request-bearing fragment's parameter name. An unconditional
`Use(fragment)` preserves that name. To share one request with validation, loading, sorting,
projection, limits or `UseContextKey`, explicitly align it:

```csharp
var approvedWindow = minimum.And(maximum).WithRequestName("request");
entity.HasQuery("Window").Use(approvedWindow).Select(item => item.Id).List();
```

`WithRequestName` returns a copy. Use an ordinary C# identifier; keywords are escaped by the
existing renderer. Infrastructure names and the `__repoGen` prefix retain their existing restrictions.

`UseIf(condition, fragment)` instead binds the fragment to **the condition's request name**. In
the first example, all three independently named fragments become one `SearchRequest request`
input, shared with validation. A parameterless fragment can also be conditionally applied by a
request condition. Separately used fragments are not automatically merged just because their
request CLR types match: deliberately different parameter names still describe different inputs.

## DTO, grouped and scalar fragments

The model-level factory creates a fragment without registering its row as an EF entity:

```csharp
public sealed record ReportRequest(int MinimumCount);

var enoughRows = modelBuilder.HasFragment<CategoryTotal, ReportRequest>("EnoughRows",
    (total, criteria) => total.Count >= criteria.MinimumCount);
var positiveTotal = modelBuilder.HasFragment<CategoryTotal>("PositiveTotal",
    total => total.Total > 0);
var positiveAmount = modelBuilder.HasFragment<int>("PositiveAmount", amount => amount > 0);

entity.HasQuery("CategoryTotals")
    .GroupBy(item => item.Category, group => new CategoryTotal
    {
        Category = group.Key,
        Count = group.Count(),
        Total = group.Sum(item => item.Amount)
    })
    .Results<CategoryTotal>()
    .UseIf<ReportRequest>(request => request.MinimumCount > 0,
        enoughRows.And(positiveTotal))
    .OrderBy(total => total.Category)
    .Paged();

entity.HasQuery("PositiveAmounts")
    .Select(item => item.Amount)
    .Use(positiveAmount)
    .OrderBy(amount => amount)
    .List();
```

An active grouped predicate remains in the SQL pipeline and can translate to `HAVING`. When the
condition is false, that entire filter is omitted rather than sent as an always-true SQL branch.
Counted pages count the filtered groups; dictionaries/lookups, scalar terminals and streaming
retain their existing semantics and lease lifetime.

`Use` and `UseIf` also work on result builders after `From(...)`, `FromProvider(...)`, `Select(...)`
or another grouping stage. Select the fragment's row type to match the **current** stage. After
`ProjectTo<T>()`, use `Results<T>()`; entity-builder predicates still target the entity. An old
result builder cannot be reused after a shape-changing `Select`/`GroupBy`.

Duplicate **model-level** registrations with the same trimmed name, row type and request type
fail with `RGD2201`. Different row/request types or case-distinct names are separate registrations.
Invalid request aliases fail with `RGD2202`. These factories return handles, not a name-based
lookup mechanism; composition does not register another model entry.

## Policies and boundaries

- `UseIf` is optional query filtering, not authorization or required visibility. A required root
  visibility policy still applies independently, even when all optional clauses are inactive.
- Commands can unconditionally `Use` composed fragments, just as before. There is intentionally
  no conditional command-fragment convenience that could silently broaden a write. Stage/save and
  set-based execution keep their existing, distinct behavior.
- Existing type adapters expand inside composed predicates before rendering. Inspectable
  navigation/source dependencies still contribute to cache invalidation and optional navigation
  review; composing a predicate does not certify a navigation's visibility policy.
- Request nullability and validation remain explicit. `UseIf` does not validate an entire request
  or make a dereferencing condition null-safe. Add a structural `Validate` guard or an explicitly
  null-safe condition where appropriate. Validation precedes context acquisition; optional query
  conditions choose clauses when the operation constructs its query.
- Translation belongs to EF/the provider. Computed DTO getters, arbitrary methods and constructor
  shapes may be untranslatable in filters. Use SQL-composable intermediate member-initializer
  shapes; there is no client-filter fallback. An inactive unsupported filter is never attached.
- Reusable scalar/DTO fragments do not turn those types into EF entities. Final closed inputs and
  results must still pass the ordinary public/EF-free contract checks.

The [Membership example](../../../samples/ConfigurationExamples/Membership/Configuration/MembershipDirectoryRepositoryDesigner.cs)
demonstrates this on a simple borrowed context with no cache or tenancy. Tests execute generated
SQLite queries for request binding, AND/OR/NOT, inactive clauses, DTO/scalar/group roots, paging,
dictionaries, streams, command reuse, keyed selection, registered roots, adapters and actual
related-write cache invalidation. They compile all supported surface/layout combinations, including
EF-free contracts, and preserve equivalent unconfigured output.

See [result pipelines](RESULT-QUERY-PIPELINES.md), [membership queries](MEMBERSHIP-QUERIES.md),
[tracking policies](../repositories/TRACKING-POLICIES.md) and [scoped caching](../repositories/SCOPED-CACHING.md) for their
independent configuration and provider limits.
