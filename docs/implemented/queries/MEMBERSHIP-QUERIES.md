# Designer-defined list membership

`WhereIn` and `WhereNotIn` configure request-bound set filters on named operations. Services call
the generated method; they do not assemble `Contains`, normalize inputs, or choose database SQL.
This is optional. Existing `Where(...Contains...)` expressions and unconfigured/simple output are
unchanged, with no new CLI flag, runtime package, or injected dependency.

```csharp
entity.HasQuery("FindByIds")
    .WhereIn(item => item.Id, (int[] ids) => ids,
        options => options.Distinct().MaximumCount(1000))
    .OrderBy(item => item.Id)
    .ProjectTo<ItemSummary>()
    .List();

// Application service:
var items = await repository.FindByIdsAsync(ids, cancellationToken);
```

The row selector can be a provider-translatable scalar expression, not just a direct property.
The values selector accepts one typed request and returns `IEnumerable<TValue>?`. Use a named
request object to share its other fields with predicates, sorts, projections, loading, validation,
and context selection. Registered type-adapter markers in either selector expand at design time.
No designer code is invoked to obtain a runtime list.

## Defaults and deliberate overrides

| Option | Default | Effect |
| --- | --- | --- |
| `NullInput(...)` | `Reject` | A null collection throws `ArgumentNullException` |
| `EmptyInput(...)` | `EmptySet` | Apply membership against an empty set, without removing the filter |
| `NullValues(...)` | `Include` | Preserve null elements and let normal EF/provider null semantics apply |
| `Distinct(...)` | `false` | Optionally deduplicate using the CLR default equality comparer, preserving first-occurrence order |
| `MaximumCount(...)` | `null` | Optionally bound the number of raw values, before dropping nulls or duplicates |

`RepositoryMembershipInputBehavior` has three choices:

- `EmptySet`: `WhereIn` matches nothing; `WhereNotIn` excludes nothing. This is ordinary set
  membership, so the same empty-set policy has deliberately different positive/negative results.
- `IgnoreFilter`: omit **only this membership predicate**. Other predicates, visibility policies,
  and EF global query filters remain active. Choose this explicitly when absence/empty means
  "no selection constraint"; it is not an authorization bypass.
- `Reject`: throw before entering the generated database operation.

Null and empty policies are independent. `NullInput(EmptySet)` is not subsequently rejected by
`EmptyInput(Reject)`. For a non-null input, the empty policy runs after normalization; a list
containing only ignored null values is therefore empty.

`RepositoryMembershipNullValueBehavior` is `Include`, `Ignore`, or `Reject`. It applies to nullable
value types as well as reference values; non-nullable value types cannot contain null. Default
EF null compensation is retained, including negated membership over a nullable column. Explicit
provider choices such as `UseRelationalNulls` can change SQL semantics; RepoGen does not override
them. Nor does CLR deduplication redefine database collation/equality. There is no custom comparer
that could silently collapse database-distinct values.

The count limit is non-negative; zero permits only empty inputs. Passing `null` disables it. The
limit counts every raw value, even duplicates and values subsequently ignored. This bounds a
duplicate-heavy/unbounded enumerable instead of merely limiting its number of unique results.
It may pull the first excess value to detect overflow, then disposes enumeration and throws
`ArgumentOutOfRangeException`. No rows are queried in response to an invalid input.

For an optional search selection:

```csharp
public sealed record SearchRequest(int MinimumId, string?[]? Categories);

entity.HasQuery("Search")
    .Validate<SearchRequest>(request => request.MinimumId >= 0, "MinimumId must be non-negative.")
    .Where<SearchRequest>((item, request) => item.Id >= request.MinimumId)
    .WhereIn(item => item.Category, (SearchRequest request) => request.Categories,
        options => options.NullInput(RepositoryMembershipInputBehavior.IgnoreFilter)
            .NullValues(RepositoryMembershipNullValueBehavior.Ignore)
            .Distinct().MaximumCount(100))
    .OrderBy(item => item.Id)
    .ProjectTo<ItemSummary>()
    .List();
```

Here null means no category restriction, but an empty array still returns nothing. Rejecting null
is the default for ordinary ID lookups. Negative filters can deliberately opt into `EmptyInput(Reject)`
when an empty exclusion list must be considered a caller error.

Runtime behavior and public nullable annotations are separate. Expression trees erase reference
annotations: use existing `AllowNullInput`/`InputNullability` when the collection itself is an
operation argument, rather than a property of a reflected request DTO. For example:

```csharp
entity.HasQuery("FindByNames")
    .WhereIn(item => item.Name, (string?[] names) => names)
    .InputNullability<string[]>("names", type => type.Element(element => element.Nullable()))
    .Select(item => item.Id).List();
```

## Result stages, groups, and infrastructure roots

The same API is available after `Select`, `Results`, grouping, and `From`/`FromProvider`. Its
position in a result pipeline is retained. For example, this generates a membership filter on
the aggregate result, not on each input row:

```csharp
entity.HasQuery("CategoryCounts")
    .GroupBy(item => item.Category,
        group => new CategoryCount { Category = group.Key, Count = group.Count() })
    .Results<CategoryCount>()
    .WhereIn(total => total.Count, (int[] counts) => counts)
    .OrderBy(total => total.Category)
    .List();
```

`WhereNotIn` takes the same arguments/options and negates membership. LINQ translation is still
provider-owned: unsupported keys/result shapes fail with the normal provider exception, without
client-side row filtering. Use a translatable intermediate projection before constructing a DTO
whose constructor members the provider cannot filter on.

Entity membership must precede `Results`/replacement roots. Continue from the current result
builder for result membership; stale builders cannot place a filter on the wrong row type.
Membership selector navigation dependencies participate in inferred cache dependencies. This
does not extend root-only RepoGen visibility policies to navigations; use EF global filters or
review the related-source filtering explicitly, as for other navigation expressions.

## Execution, lifetime, and caching

Each configured binding is evaluated and snapshotted once per operation invocation (once per
enumeration for a closed stream), with cancellation checks during enumeration. The input collection
is not mutated. This is input preparation, not entity materialization: generated `Contains`
predicates still execute server-side. A page's count and data queries reuse the same prepared
array, not the original enumerable.

Validation/preparation happens before closed context/keyed-lease acquisition, before a compatibility
resolver is used, and before cache lookup. Invalid input cannot be hidden by a cached result. Closed
streams retain their existing deferred execution and dispose an owned context on early exit,
cancellation, or failure. Multiple bindings are prepared independently; if several selectors or
other request clauses reuse one enumerable, provide a repeatable materialized collection.

Cached membership queries require an array, `ICollection<T>`, or `IReadOnlyCollection<T>` as the
values expression's static body type. Cache-key serialization also examines the original method
arguments; a one-shot enumerable cannot safely be serialized again. Materialize it in the
application request, or disable caching for that operation. This validation applies only when
caching is actually enabled; an explicitly disabled cache does not restrict uncached inputs.
Requests must retain the existing cache contract of stable, serializable, repeatable data.

Cache keys include prepared values and the apply-filter flag, as well as ordinary arguments and
operation identity. Distinct input arrays are not promised a canonical shared cache key. Existing
scoped/commit-coordinated invalidation and legacy decorator behavior are retained. Prepared arrays,
flags, and helper entry points are internal and never added to closed application ports, bound
port signatures, or public API snapshots.

## Large lists and provider boundaries

RepoGen emits ordinary parameterized `Contains` over the prepared array. It does not interpolate
values into SQL, split one logical query into independently paged batches, install a table type,
or silently choose a TVP/temp-table strategy.

With the pinned EF Core 8 SQL Server provider and compatible database configuration, actual fixture
tests verify a 6,000-value integer array transported through one `OPENJSON` parameter, including
counted paging. Nullable/negated string membership and grouped `HAVING` filters are executed too.
Those tests explicitly configure EF compatibility level 150 and need a SQL Server database capable
of that syntax. This is verified provider behavior, not a performance guarantee for all list
sizes/database compatibility levels. Core/design projects gain no SQL Server dependency.

If a deployment requires TVPs, temp tables, or specialized large-list SQL, configure an explicit
[registered infrastructure query root](../repositories/PROVIDER-CAPABILITIES.md) using the supplied operation
context and parameterized inputs. Result membership can continue after that root. Deployment of
SQL types, schema permissions, provider translation, and list-size/performance policy remain
explicit infrastructure concerns; RepoGen does not assume them from a `Contains` expression.

## Validation and example

- `RGD1901`: anonymous/open/delegate key or statically queryable/string value input.
- `RGD1902`: membership configured on a superseded entity/root builder.
- `RGD1903`: enabled caching would require re-enumerating an unmaterialized membership input.

An `IQueryable` hidden behind `IEnumerable` is also rejected at runtime before context acquisition,
rather than unexpectedly materializing a second database query. Closed input validation includes
membership bindings: EF entities/contexts/queryables cannot escape through their requests.

The [Membership example](../../../samples/ConfigurationExamples/Membership/Program.cs) uses a simple,
borrowed-context EF-free application port. Its [designer](../../../samples/ConfigurationExamples/Membership/Configuration/MembershipDirectoryRepositoryDesigner.cs)
configures ID lookup, optional-category search, grouping, visibility, normalization, and limits.
It needs no cache, tenancy, context lease, unit of work, or SQL Server package. Run all examples:

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Check -Run
```
