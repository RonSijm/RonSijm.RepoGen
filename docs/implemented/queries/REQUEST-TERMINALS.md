# Request-bound dictionary, lookup and aggregate terminals

Result builders can use the operation's typed request in `Dictionary`, `Lookup`, `Min`, `Max`,
`Sum` and `Average` mappings. These are optional designer overloads, not service-side extensions
or runtime expressions. Existing row-only terminals and unconfigured generation are unchanged.

```csharp
public sealed record AmountRequest(int Multiplier, string LabelSuffix, string? Region);

entity.HasQuery("TotalsByRegion")
    .Validate<AmountRequest>(request => request.Multiplier >= 0,
        "Multiplier must be non-negative.")
    .Results<Sale>()
    .GroupBy(sale => sale.Region,
        group => new { Region = group.Key, Total = group.Sum(sale => sale.Amount) })
    .Dictionary<string, int, AmountRequest>(
        (total, request) => total.Region + request.LabelSuffix,
        (total, formatting) => total.Total * formatting.Multiplier);

entity.HasQuery("ScaledAmount")
    .WhereIf<AmountRequest>(request => request.Region != null,
        (sale, request) => sale.Region == request.Region)
    .Select(sale => sale.Amount)
    .Sum<AmountRequest>((amount, request) => amount * request.Multiplier);
```

The closed service calls ordinary methods:

```csharp
var totals = await repository.TotalsByRegionAsync(request, cancellationToken);
var amount = await repository.ScaledAmountAsync(request, cancellationToken);
```

The first returns `IReadOnlyDictionary<string, int>`; the second returns `int`. The service does
not choose SQL operators, build LINQ, provide delegates, or perform the dictionary projection.
The same terminals work after named projections, grouping, `From(...)`, registered roots and
intermediate result stages, including compatibility and CQRS-selector named queries.

## Overloads and input names

Dictionary and lookup key/value selectors can each be row-only or `(row, request)` expressions.
For example, keeping a fixed key while binding the value needs no request-bound key:

```csharp
results.Dictionary(row => row.Id,
    (ItemSummary row, LabelRequest request) => row.Name + request.Suffix);

results.Lookup(row => row.Category,
    (ItemSummary row, LabelRequest request) => row.Name + request.Suffix);

// A key-only overload keeps the approved result row as the value.
results.Dictionary<int, OffsetRequest>((row, request) => row.Id + request.Offset);
```

Use explicit generic arguments when inference cannot discover the request type, particularly
for anonymous intermediate rows. A request-bound key/value pair uses
`Dictionary<TKey, TValue, TRequest>` or `Lookup<TKey, TValue, TRequest>`; a key-only terminal uses
`Dictionary<TKey, TRequest>` or `Lookup<TKey, TRequest>`.

When both selectors use a request, the value selector is rebound to the key selector's request
parameter, even if the lambdas use different local names. This avoids two copies of the same
input and preserves nested lambda scopes. If only the value uses a request, its name is retained.
Other query clauses share an input when their request type and parameter name match; use the
same name across root predicates, sorting, guards and terminals. Different named inputs remain
distinct. Reserved generated/infrastructure parameter names are rejected.

Terminal-only inputs are registered before nullability annotations, port binding, API/data
snapshots and manifest capture. They cannot bypass the closed contract's EF-free type checks.
An existing handwritten port must declare the resulting input and output signature exactly.

## Scalar types and provider behavior

`Min<TValue, TRequest>` and `Max<TValue, TRequest>` retain the selected value type. `Sum<TRequest>`
and `Average<TRequest>` offer the existing numeric and nullable numeric selector families:

| Selector value | Sum result | Average result |
| --- | --- | --- |
| `int`, `long` | Same selected type | `double` |
| `float`, `double`, `decimal` | Same selected type | Same selected type |
| Nullable numeric | Nullable corresponding sum type | Nullable corresponding average type |

Expressions are bound to the generated query; request values are provider parameters, not
interpolated SQL text. Fixed arithmetic, aggregation, grouping, filtering and supported ordering
execute on the provider. The selected EF provider still decides which mappings and numeric types
translate. For example, SQL Server decimal sum/average execute in the relational fixture;
compiling a decimal overload does not promise SQLite can execute that aggregate.

Scalar terminals issue one aggregate query and retain EF empty/null/cardinality semantics. The
SQLite tests cover empty sum returning zero, nullable min/average returning null, and non-nullable
max failing on an empty source. An untranslatable aggregate fails through EF, with no client-side
aggregate fallback. Request arithmetic does not add checked-overflow guarantees or domain rules.

Dictionary/lookup terminals project the approved pair, buffer one result query, then assemble
the keyed result. EF's normal final projection may construct a DTO or typed ID after selecting
its columns; this is not a fallback for server predicates or aggregates. Whole-row values must
already be approved application shapes in closed mode, not EF entities.

## Existing policies remain in force

- Dictionary duplicates still throw by default. Explicit `KeepFirst`/`KeepLast` require guaranteed
  order; lookups preserve all duplicate values. Request-dependent keys can introduce collisions
  or nulls, and null keys fail under every policy. See [Dictionary results](DICTIONARY-RESULTS.md).
- Inputs and outputs can use explicit outer/nested [nullability](../repositories/CONTRACT-NULLABILITY.md). Nullable
  inputs require guarded mappings; annotations do not invent null behavior.
- Registered [type adapters](../repositories/TYPE-ADAPTERS.md) are inlined inside terminal mappings. Captured
  designer state remains invalid; no runtime mapping service or compiled delegate is generated.
- Terminal navigation expressions participate in visibility review and inferred cache dependencies.
  Review is not filtering or authorization. A related write invalidates a cached mapped value.
- Optional closed cache keys include the terminal-only arguments and the mapped query shape.
  Compatibility decorators retain their existing method/argument identity and policy fingerprints;
  these overloads do not retrofit closed-cache scope/commit guarantees onto them. Closed result
  cloning, scope isolation and commit invalidation apply. Requests should be stable value
  objects whose serialization represents their inputs; arbitrary changing getters or ignored
  properties are not automatically snapshotted by these overloads.
- Structural `Validate(...)` guards and cancellation run before owned acquisition. SQL timeout
  policies, shared transactions, command-side reads and owned-context cleanup retain their existing
  semantics. A read terminal does not save pending changes.

No new model-wide switch, runtime dependency, provider package or CLI option is required. Opt in
by choosing the request-aware terminal; simple row-only generation stays byte-identical.

The [Reports designer](../../../samples/ConfigurationExamples/Reports/Configuration/ReportingRepositoryDesigner.cs)
and [program](../../../samples/ConfigurationExamples/Reports/Program.cs) demonstrate grouped label/value
mappings, a shared request across optional filtering and sum, pre-acquisition guards, inherited
timeouts and owned leases behind the application-owned `ISalesReports` port.
