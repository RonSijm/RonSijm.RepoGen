# Designer-defined navigation loading

Loading profiles are optional design-time configurations. They keep reference/collection paths,
filters, and query strategy out of services. Registering a profile does **not** apply it to any
query: attach it deliberately to a named operation, a configured projection, or an entity default.
Existing simple repositories and compatibility defaults are unchanged.

## Typed paths and reusable profiles

Use `Load(...)` when a path belongs to one operation, or `HasLoadProfile(...)` to reuse it:

```csharp
var entity = modelBuilder.Entity<Order>();
var detail = entity.HasLoadProfile("Detail", load =>
{
    load.Include(order => order.Customer)
        .ThenInclude(customer => customer!.Address);
    load.Include(order => order.Lines)
        .ThenInclude(line => line.Product);
});

entity.HasQuery("GetDetails")
    .UseLoadProfile(detail) // Alternatively: UseLoadProfile("Detail").
    .Load(load => load.Include(order => order.Notes))
    .Where<int>((order, id) => order.Id == id)
    .AsReadOnly()
    .AsSplitQuery()
    .RequireExplicitLoadStrategy()
    .ProjectTo(order => OrderMapping.Details(order))
    .SingleOrDefault();
```

`OrderMapping.Details` in this example is an explicit final entity-graph-to-DTO conversion.
The loaded relationships must actually be used by the chosen result mapping. Closed-operation
consumers call only `GetDetailsAsync(id, cancellationToken)`; they receive no profile chooser or
`Include`/`ThenInclude` API.

Collection `ThenInclude` lambdas select an **element**, including `List<T>`, `ICollection<T>`,
and filtered `IEnumerable<T>` paths. Reference, nested collection, owned, and many-to-many skip
navigations are validated against the final EF model. Nullable reference paths can use `!` in
the designer; generation handles intermediate reference nullability without adding lazy loading.

Start another `Include` for a branch. Reusing an already-extended chain to append a different
branch is rejected instead of silently changing the first branch. Duplicate/equivalent paths are
safe; different filters on the same collection are rejected even when declared in different
profiles. Equivalent filters may use different nested lambda parameter names.

Profile names are case-sensitive and unique within an entity, including across configuration
modules. String references can precede their registration and resolve when the designer builds.
A typed profile handle must belong to the same entity registration in that designer.

## One runtime request, including filtered continuations

```csharp
entity.HasLoadProfile<DetailRequest>("FilteredLines", load =>
{
    load.Include((order, request) => order.Lines
            .Where(line => line.Quantity >= request.MinimumQuantity)
            .OrderBy(line => line.Position))
        .ThenInclude((line, request) => line.Notes
            .Where(note => note.IsPublic || request.IncludeInternalNotes)
            .OrderBy(note => note.Id)
            .Take(request.MaximumNotesPerLine));
});

entity.HasQuery("GetFilteredDetails")
    .UseLoadProfile("Detail")
    .UseLoadProfile("FilteredLines")
    .Where<DetailRequest>((order, request) => order.Id == request.Id)
    .AsReadOnly().AsSplitQuery()
    .ProjectTo(order => OrderMapping.Details(order))
    .SingleOrDefault();
```

The operation takes one `DetailRequest request`, not a parameter for every filter. Existing
name/type-based request binding is shared with predicates, sorting, result stages, and limits.
Inputs used only by loading still become operation parameters, including on bound application
ports. Use the same request name/type throughout to share it; conflicting bindings fail validation.

For a one-off operation, use `HasQuery(...).Load<DetailRequest>(load => ...)` instead of registering
a profile. Request builders also accept parameterless `Include` and `ThenInclude` lambdas; these
preserve the request type for a later two-argument filtered continuation.

Supported collection operators are `Where`, `OrderBy`, `OrderByDescending`, `ThenBy`,
`ThenByDescending`, `Skip`, and `Take`, subject to the provider's translation capabilities.
`Select`, `ToList`, scalar/unmapped properties, filtering a reference, and captured designer
state are not loading paths. Configure root filters separately from collection filters: filtering
the included children does not filter out the parent.

Parameterized loading is supported on named queries only. Entity defaults and standalone
projection methods have no request parameter, so attaching a parameterized profile there produces
a clear diagnostic. Validate request bounds and permissions in the application; RepoGen does not
infer domain validation or authorization rules from a loading expression.

## Projections are not entity graph loading

A SQL projection can traverse navigations without materializing a full graph:

```csharp
entity.HasProjection<OrderSummary>(order => new OrderSummary(
        order.Id,
        order.Customer == null ? null : order.Customer.Name))
    .AutoIncludeNavigations(false);
```

Automatic root-navigation detection remains enabled by default for compatibility. Disable it
per projection with `AutoIncludeNavigations(false)` when SQL projection alone supplies the shape.
Explicit loading still applies. Unparameterized profiles can also be attached to projections:

```csharp
entity.HasProjection(order => OrderMapping.Details(order))
    .UseLoadProfile("Detail")
    .AutoIncludeNavigations(false);
```

EF can prune includes from a pure DTO/scalar `Select`. A filtered include is **not** a substitute
for filtering a collection inside that projection. Put the filter in `Select` for a SQL DTO shape,
or use an explicit final conversion of a deliberately loaded entity graph as in the Library
example. Never assume an include makes an opaque mapping function SQL-translatable. These are
EF loading semantics, not additional RepoGen evaluation rules. See Microsoft's
[eager-loading documentation](https://learn.microsoft.com/en-us/ef/core/querying/related-data/eager).

There is no silent client-side filter fallback. Query translation errors propagate from the
provider. For example, some filtered/limited nested graphs require SQL `APPLY` and cannot execute
on SQLite. The tests compare one such shape with raw EF and verify that generated operations
dispose an owned lease on failure. Passing design/model validation is not a guarantee of SQL
translation on every database.

## Defaults, tracking, and query strategy

- Named-query `IgnoreAutoIncludes()` optionally disables user-configured EF model automatic
  loading, retaining explicit paths/profiles and RepoGen projection inference. It is also
  available on the current projected/grouped/From result builder. Query filters, owned loading
  and tracking fix-up stay independent. See [loading policies](EXPLICIT-LOADING-POLICIES.md)
  and [MemoryLoadingPolicies](../../../samples/ConfigurationExamples/MemoryLoadingPolicies/README.md).
- `entity.Load(...)` and `entity.UseLoadProfile(...)` apply unparameterized paths to ordinary
  entity-root lookups/queries. They do not apply to a replacement `From(...)` root. Attach loading
  to the named query when it is not an intentional entity-wide default.
- Profiles contain loading paths only. Configure `AsReadOnly`, tracking, query tags, query-filter
  bypass, and split/single strategy on the operation; no profile silently overrides these policies.
- `RequireExplicitLoadStrategy()` defaults to off. When enabled on a named query with multiple
  distinct collection paths, choose `AsSplitQuery()` or `AsSingleQuery()` explicitly. Validation
  also considers paths from projections and entity defaults. Counting collection paths is a guard,
  not a cost estimate or automatic optimization.
- Counted paging remains a count plus item query; split loading can add more item queries. For a
  stream, the owned lease lasts through enumeration or early disposal. Final DTO graphs remain
  usable after disposal without lazy loading.
- Closed read contracts default to no-tracking. With legacy tracked contexts, EF relationship
  fixup can make previously tracked children appear in a filtered navigation. Configure the
  read policy deliberately. No-tracking is not immutability or an authorization boundary. See
  [Tracking policies](../repositories/TRACKING-POLICIES.md).

Declared nested paths contribute dependency tags for legacy configured caching, including skip
navigation targets. Navigation references in SQL projections contribute dependencies independently
of automatic includes. Opaque mapping functions still need explicit loading/dependency declarations;
the generator does not inspect arbitrary method bodies. These dependencies also participate in
the optional [closed-operation scoped cache](../repositories/SCOPED-CACHING.md).

## Runnable example and verification

[LibraryRepositoryDesigner](../../../samples/ConfigurationExamples/Library/Configuration/LibraryRepositoryDesigner.cs)
declares optional author/address loading, chapters with request-driven nested note filters/limits,
and filtered reviews. Its [EF-free port](../../../samples/ConfigurationExamples/Library/Contracts/LibraryContracts.cs)
exposes just one named method; the [consumer](../../../samples/ConfigurationExamples/Library/Program.cs)
checks rebinding, missing results, a detached DTO graph, no tracking, and exactly-once owned disposal.

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Run
```

Manifests inventory profile names, effective nested loading paths, collection/filter flags, and
the selected query strategy. Tests compile and execute generated code on SQLite, inspect SQL and
column pruning, and cover single/split strategies, paging, streaming cancellation/disposal,
parameter-only loading inputs, ports, conflicting filters, stale chains, and invalid profiles.
Optional SQL Server fixtures also verify filtered nested graph loading and request limits,
single/split/paged round trips, projection pruning, global-filtered optional references, and
owned stream cancellation after split-query buffering. The optional
[navigation visibility guard](NAVIGATION-VISIBILITY.md) records policy/reason review receipts;
loading/cache dependencies do not themselves filter navigation rows or authorize callers.
