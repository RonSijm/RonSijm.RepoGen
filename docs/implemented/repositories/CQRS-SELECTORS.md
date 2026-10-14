# Optional CQRS selector repositories

`UseCqrsSelectors()` adds separate read/write capabilities to the design-time tool's selector
surface. It is off by default in both presets. It needs no tenant resolver, cache provider, async
lease or unit-of-work service: an existing borrowed, scoped context is enough.

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults().UseCqrsSelectors());
```

This is an alternative to `UseClosedRepositories(...)`, not an additional flag to combine with it.
Closed operations remain the choice when services should see only reviewed, named operations and
EF-free contracts in a separate application project.

## Generated capabilities

| Surface | Capabilities |
| --- | --- |
| `IReadXRepository` | Key/index lookups, configured filters, collections and configured named read operations |
| `ReadXSelector` / `ReadXCollectionSelector` | Reads and typed read chains; no mutation, staging, save, set-based writes or raw query hook |
| `IWriteXRepository` | Existing configured write helpers, named commands, optional named command reads, and selectors that can load/query entities for mutation |
| `IXRepository` / cacheable backing interface | Internal persistence implementation detail in this mode, not a public combined escape hatch |

Read selectors are sealed wrappers, not subclasses of write-capable selector bases. Filtering,
ordering, limiting, includes and optional tracking controls preserve the read selector type.
They cannot be cast to `BaseEntitySelector<TEntity>` to obtain a write API. Neither the read port
nor its selectors exposes `DbContext`, `IQueryable`, `ConfigureQuery` or cache callbacks.

Common reads remain available: entity/DTO materialization, first-or-default, count/exists,
dictionaries, offset/cursor paging, grouping/aggregates and cancellable streams. Dynamic sorting
still requires `HasSortableFields(...)`. `ProjectionEnabled = false` removes ad-hoc projection
and grouping entry points from the read selectors altogether; configured projections/queries
remain available. This does not prune every individual scalar aggregate or read helper.

The write port deliberately permits reads: commands may need to reconstruct an aggregate or
select a collection for a mutation. It does not inherit the read port, whose selectors have
different capabilities and tracking defaults. Advanced write helpers still follow their existing
generation switches. In particular the compatibility retry signature remains EF-facing.

## Keep service queries in the designer

Selecting this surface does not require services to assemble chains:

```csharp
modelBuilder.Entity<WorkItem>(entity =>
{
    entity.ProjectionEnabled = false;
    entity.HasProjection<WorkItemSummary>(item =>
        new WorkItemSummary(item.Id, item.Title, item.Priority));
    entity.HasQuery("ListOpen")
        .Where(item => !item.Completed)
        .OrderBy(item => item.Priority).ThenBy(item => item.Id)
        .ProjectTo<WorkItemSummary>().List();
    entity.HasCommand("Complete").Where<int>((item, id) => item.Id == id)
        .Patch(setters => setters.SetProperty(item => item.Completed, item => true))
        .Stage();
});
```

The consumer calls `reads.ListOpenAsync(cancellationToken)` or
`writes.CompleteAsync(id, cancellationToken)` directly. Named reads are instance methods on the
read port by default, with the same inputs/results/validation as their configured query.
`HasQuery(...).OnWriteRepository()` instead exposes a named command-side read on the write port,
using the ordinary write tracking policy (or an explicit designer override), without cache hits.
Named commands remain on the write port. See [Command-side queries](../queries/COMMAND-SIDE-QUERIES.md).
The example's staged command returns `CommandStatus.Staged`, **not**
saved success; save explicitly after staging the participating changes.

## Detached reads by default

Ordinary read selectors and named read operations default to no-tracking independently of the
write selector/context defaults. This includes entity materialization inside projections and
replacement `From(...)` roots. Existing tracked entries are not detached or overwritten.

```csharp
modelBuilder.Configure(options => options.UseCqrsSelectors()
    .UseReadOnlyTracking(RepositoryTrackingBehavior.NoTrackingWithIdentityResolution));
```

`Tracking` is an explicit opt-in. `Unspecified` inherits `UseDefaultTracking(...)`, then the context
policy. An explicit named query tracking policy takes precedence. Optional runtime controls use
the existing `GenerateTrackingMethods(...)` / `UseTrackingMethodNames(...)` settings; the simple
preset hides them. Tracking is independent from repository capability segregation: returned
objects are not immutable, and deliberately tracked entities can still be changed/saved through
another owner of that context. This is not an authorization/security boundary.

## Entity selection

```csharp
modelBuilder.Entity<ReferenceLabel>().WriteEnabled = false;
modelBuilder.Entity<RecordedEvent>().ReadEnabled = false;
```

These omit the corresponding public ports and operations. Queries follow their selected target;
disabled queries are not resolved, and disabled commands do not require valid writer configuration. Keyless entities automatically have
no write port/public write selectors. Active keyless commands produce `RGD1604` instead of being
silently advertised as working writes. Entity allowlisting/exclusion options continue to apply.

Application-owned port binding, separate EF-free contracts and API/data-shape snapshots remain
closed-operation features. Selector contracts still refer to EF model classes and expression
builders, and live with their generated implementation; they are not the EF-free contract mode.

## Registration and lifetime

```csharp
services.AddDbContext<WorkboardContext>(/* ordinary application configuration */);
services.AddWorkboardContextSelectorRepositories();
```

The generated registration is in `RonSijm.RepoGen`. Scoped ports share one backing repository per
entity and the application's scoped context across entities. Separate service scopes do not share
pending tracker changes. Existing registrations are preserved (`TryAddScoped`); call with the
intended options initially, because subsequent calls do not replace the first registrations.

`SaveChangesAsync` saves **every tracked change in that shared context**, including changes staged
through other write repositories. It is not scoped to one table. N staged writes are not a
promise of one SQL statement or one round trip. Query operations/streams borrow the context;
the container/application controls its lifetime. Do not let selectors/streams outlive that scope,
or run parallel operations on the same context.

`UseDbContextResolver()` remains optional; register its interface explicitly and ensure it returns
the same borrowed context throughout a shared save. Async owned leases/keyed leases require closed
operations rather than selectors that could escape after disposal.

If `GenerateCacheDecorator()` is selected, registration accepts `useCacheDecorator: true` and
then requires the existing generated `IRepositoryCacheProvider`. Its named cached queries and
fluent-write invalidation callbacks are preserved. Merely generating the decorator adds no
construction-time cache dependency. This is the legacy selector cache policy, **not** the scoped,
commit-coordinated `UseClosedCaching()` adapter; it does not acquire distributed/transactional
durability guarantees.

## Generation and examples

The code-first `RepositoryGenerator.Generate(context, designer)` handles this option with its
usual selector-generation default. For the CLI, use the designer and `--generate-selectors` when
replacing existing selectors. Attribute-generated/reused selectors cannot silently adopt this
surface; `RGD1602` requires removing the attribute/rebuilding or regenerating the tool-owned types.
Both single-file and per-entity output work; per-contract output/snapshots remain closed-only.

The [Workboard designer](../../../samples/ConfigurationExamples/Cqrs/Configuration/WorkboardRepositoryDesigner.cs)
and [CQRS application](../../../samples/ConfigurationExamples/Cqrs/Program.cs) demonstrate read/write-only
entities, a keyless view, named operations, detached entities and a shared two-repository save.

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Check -Run
```
