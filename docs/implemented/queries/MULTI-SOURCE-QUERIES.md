# Designer-defined multi-source queries

`HasQuery(...).From(...)` lets a designer configure a query over several typed EF sets. Services
still call one generated named operation; they do not receive a `DbContext`, query sources, or a
runtime expression-builder API in closed-operation mode.

This is opt-in per query. Without `From`, existing entity-root queries and generation defaults
are unchanged. There is no additional CLI flag or service registration for joins.

## An inner join without an EF navigation

```csharp
modelBuilder.Entity<Order>(entity =>
{
    entity.HasQuery("GetOrderSummaries")
        .AsNoTracking()
        .From(sources =>
            from order in sources.Set<Order>()
            join customer in sources.Set<Customer>()
                on order.CustomerId equals customer.Id
            orderby order.Id
            select new OrderSummary(order.Id, customer.Name, order.Amount))
        .List();
});
```

The owning entity determines which repository exposes the operation; it does not restrict the
root to that entity's set. Each `Set<TEntity>()` must refer to a non-owned, non-shared entity in
the selected context's EF model. Joining a set does not enable its repository: configured-entity
allowlisting and `RepositoryEnabled` still govern exposed repositories independently.

`RepositoryQuerySources` is an expression placeholder in the EF-free Design package. RepoGen
inspects the expression, replaces the typed roots with sets on the operation's context, and
generates C#. It does not instantiate this placeholder, compile/invoke the factory, or pass a
delegate to the application.

## Composite and left joins

Use ordinary LINQ composite keys:

```csharp
entity.HasQuery("GetMatches")
    .From(sources =>
        from order in sources.Set<Order>()
        join customer in sources.Set<Customer>()
            on new { Id = order.CustomerId, order.Zone }
            equals new { customer.Id, customer.Zone }
        orderby order.Id
        select new OrderSummary(order.Id, customer.Name, order.Amount))
    .List();
```

For the usual left join, flatten a group join immediately with `DefaultIfEmpty` and explicitly
map missing right-side values:

```csharp
entity.HasQuery("GetOrdersWithOptionalCustomers")
    .From(sources =>
        from order in sources.Set<Order>()
        join customer in sources.Set<Customer>()
            on order.CustomerId equals customer.Id into customers
        from customer in customers.DefaultIfEmpty()
        orderby order.Id
        select new OptionalOrderSummary(
            order.Id,
            customer == null ? null : customer.Name,
            customer == null ? (int?)null : customer.Id))
    .List();
```

Use nullable DTO members for absent values. The generator does not replace a missing integer key
with zero or discard an unmatched left row. Group-join collections are not automatically loaded
or materialized: provider translation still determines supported query shapes.

## Correlated existence, anti-joins, and set operations

Sets can appear inside correlated expressions:

```csharp
entity.HasQuery("GetOrdersWithoutCustomers")
    .From(sources => sources.Set<Order>()
        .Where(order => !sources.Set<Customer>().Any(customer =>
            customer.Id == order.CustomerId && customer.Zone == order.Zone))
        .Select(order => order.Id))
    .OrderBy(id => id)
    .List();
```

Normal `SelectMany`, `Union`, `Concat`, `Except`, and `Intersect` can also be configured in the
expression. Match the participating projections' row types; SQL support is provider-owned.
`Union` de-duplicates and `Concat` preserves duplicates; RepoGen does not interchange them.

Every root uses the **same captured context**. In resolver mode it is resolved once, not once
per table; in async lease mode the whole query uses one lease. This does not provide
cross-database/provider federation.

## Request-bound roots and continued result pipelines

The two-parameter overload infers both types from explicitly typed lambda parameters:

```csharp
entity.HasQuery("GetCustomerTotals")
    .AsNoTracking()
    .From((RepositoryQuerySources sources, ReportRequest request) =>
        from order in sources.Set<Order>()
        where order.Amount >= request.MinimumAmount
        join customer in sources.Set<Customer>()
            on order.CustomerId equals customer.Id
        select new { Customer = customer.Name, order.Amount })
    .GroupBy(row => row.Customer, group => new
    {
        Customer = group.Key,
        Count = group.Count(),
        Total = group.Sum(row => row.Amount)
    })
    .WhereIf<ReportRequest>(request => request.MinimumCount > 0,
        (total, request) => total.Count >= request.MinimumCount)
    .OrderByDescending(total => total.Total)
    .ThenBy(total => total.Customer)
    .Select(total => new CustomerTotal(total.Customer, total.Count, total.Total))
    .Paged();
```

The root and result stages share one `request` argument because its name and type match.
The existing list, optional/single, scalar, dictionary, counted/uncounted page, and stream
terminals remain available. Anonymous intermediate rows are supported; choose a named DTO or
scalar before exposing a list/page/stream result. Closed inputs/results must remain public and
EF-free.

Prefer anonymous/member-initializer intermediate shapes, with constructor DTOs selected last.
In particular, SQL filtering/sorting over an arbitrary constructor DTO may not translate even
when the corresponding column expression does. RepoGen does not rewrite constructor semantics
or move a failed filter to the client. See [result pipelines](RESULT-QUERY-PIPELINES.md).

## Policies, dependencies, and diagnostics

- Configure query policies such as `AsNoTracking`, `AsTracking`, `IgnoreQueryFilters`, query
  splitting, tags, and legacy caching **before** `From`. They apply to its typed roots.
- EF global query filters remain active on participating sets unless this designer operation
  explicitly calls `IgnoreQueryFilters()`. This bypass is not an authorization system.
- `From` replaces the entity root and disables its default sort. Entity-level filters, includes,
  sorts, projections, and cursor clauses cannot be combined with it; use the source expression
  or returned result builder instead. Misplaced clauses fail rather than being silently dropped.
- Constructor DTOs may already be sorted inside the expression, before their final `select`.
  Result `ThenBy` still requires an immediately preceding result ordering stage; it does not
  assume ordering hidden inside a factory. Choose a stable unique tie-breaker for paging.
- Typed sets and visible navigation references are inferred as dependencies, including root
  policies, predicate/sort/terminal clauses and later result stages. Inspectable EF global filters
  are followed transitively unless explicitly ignored. Opaque scalar helpers and registered
  provider behavior may have further dependencies; declare those with `DependsOn<TEntity>()`.
- Optional `RequireNavigationVisibilityReview()` validates navigation access to policy-protected
  targets, independently from root policies and cache tags. Explicit typed-set joins need no
  receipt unless they also traverse navigations. See [Navigation visibility](NAVIGATION-VISIBILITY.md).
- Legacy generated cache decorators include inferred dependencies in their cross-repository
  invalidation tags. Handwritten writes or excluded repositories still require the existing
  invalidation/commit integration. Optional closed caching adds actual context scope and deferred
  commit invalidation; see [Scoped caching](../repositories/SCOPED-CACHING.md).
- Source/operator inventories are emitted in the schema-v1 JSON manifest. The operator list
  is a sorted inventory, not an exact replay of expression nesting or SQL.
- Runtime queryable inputs, opaque query-producing factories, configuration-time captured
  state, unmapped/owned/shared roots, and materialization/client boundaries such as
  `AsEnumerable`, `AsQueryable`, `ToList`, or `Compile` are rejected. Use generated terminals.
- `From` may only start once. Earlier result builders become stale after shape-changing
  `Select`/`GroupBy` stages, as with ordinary result pipelines.

The restrictions above intentionally make this an inspectable query factory, not an unrestricted
runtime callback. Nested materializing collection projections are not supported through `From`
yet; existing designer projections remain available for their supported entity/navigation shapes.
This API belongs to the design-time tool/designer workflow, not the attribute generator.

## Runnable example and verification

[ReportingRepositoryDesigner](../../../samples/ConfigurationExamples/Reports/Configuration/ReportingRepositoryDesigner.cs)
configures `GetSalesWithLabels`: a left join to a second table without exposing another
repository or requiring an EF relationship. The
[Reports application](../../../samples/ConfigurationExamples/Reports/Program.cs) verifies preserved
rows, a nullable missing label, an EF-free bound application port, and one disposed context per
operation. Regenerate/build/run with the examples' `Generate.ps1 -Configuration Release -Run`.

Tests compile and execute generated C# on SQLite and inspect SQL for inner/left/composite joins,
`EXISTS` / `NOT EXISTS`, `CROSS JOIN`, all four set operations, request-bound grouping/HAVING and
paging, and dictionary column pruning. They also verify filter bypass, tracking policies, one
resolver acquisition, streaming cancellation/disposal, dependent-cache invalidation after a
saved write, EF-free contracts, defaults, and generation diagnostics. Optional real SQL Server
fixtures also execute composite/left joins, existence/anti-joins, set operators and joined grouped
pages/dictionaries. This is fixture coverage, not a provider-wide translation guarantee. EF's
[complex-query documentation](https://learn.microsoft.com/en-us/ef/core/querying/complex-query-operators)
describes the provider-sensitive join/group patterns; its
[query-filter documentation](https://learn.microsoft.com/en-us/ef/core/querying/filters) explains
filter composition and bypass.
