# Tracking policies without EF in service APIs

Configure read behavior in the `RepositoryDesigner`. A service using a closed read contract
should call a named operation, not decide how EF's change tracker works:

```csharp
await reports.GetRegionTotalsAsync(request, page: 1, pageSize: 20, cancellationToken);
```

## Closed read contracts: no-tracking by default

`UseClosedRepositories(...)` generates `IReadXRepository`, `IWriteXRepository`, and a combined
`IXRepository`. The read interface is RepoGen's current read-only repository contract. It exposes
only configured named queries, with no EF types, selector builders, tracking controls, or saves.
`WriteEnabled = false` omits the write contract for that entity.

Closed read-target queries default to no-tracking, including entity materialization inside projections,
pages, streams, and declared `From(...)` source roots. This default is independent of
`UseDefaultTracking(...)` and the injected context's tracking default. The same named read uses
the same policy through the read, combined, or bound application interface; casting an interface
does not change behavior on a shared implementation.

Change the read default explicitly if needed:

```csharp
modelBuilder.Configure(options => options
    .UseClosedRepositories("My.Application.Repositories")
    .UseReadOnlyTracking(RepositoryTrackingBehavior.NoTrackingWithIdentityResolution));
```

`NoTrackingWithIdentityResolution` reuses instances of the same entity within a query without
adding them to the context's change tracker. `Tracking` is an explicit opt-in for special cases;
`Unspecified` opts back into `UseDefaultTracking(...)`, or the context policy if that is also
unspecified. A per-query designer policy takes precedence over either default:

```csharp
entity.HasQuery("FindSummaries")
    .AsReadOnly() // Equivalent to AsNoTracking(); optional in closed mode.
    .ProjectTo<Summary>()
    .List();

// A design-time opt-in, not a new service-side control:
entity.HasQuery("FindOtherSummaries")
    .AsReadOnly(identityResolution: true)
    .ProjectTo<Summary>()
    .List();
```

Pure scalar/DTO projections that never materialize entities do not add tracked entities anyway.
No-tracking also does not detach existing entries, discard staged changes, or make returned objects
immutable. An explicit write still stages or executes a write; a shared unit-of-work save still
saves its tracked changes. Read-only **capability** and query **tracking** are separate concepts.

Optional `HasQuery(...).OnWriteRepository()` command reads use `UseDefaultTracking(...)` (then
the context's policy when unspecified), not the read-only default. A per-query designer override
still wins. The same rule applies on the CQRS-selector surface; `.AsTracking()` can deliberately
load an editable graph without a service-side tracking control. Closed results remain EF-free.
These are reads, not locking/refresh/save operations. See [Command-side queries](../queries/COMMAND-SIDE-QUERIES.md).

## Optional neutral names on legacy selectors

Compatibility selector mode remains a combined read/write API. Compatibility/no-preset output retains the
existing `AsNoTracking`, `AsTracking`, and `AsNoTrackingWithIdentityResolution` names. The simple
preset omits all three runtime controls while retaining designer policies and ordinary CRUD.

To expose just a neutral read control:

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .GenerateTrackingMethods()
    .UseTrackingMethodNames(
        noTracking: "AsReadOnly",
        tracking: null,
        noTrackingWithIdentityResolution: null));
```

This generates `AsReadOnly()` on both single and collection selectors; the old names are not
retained as aliases. Each method returns the same concrete selector type and preserves its context,
query, and cache-invalidation callback. For example:

```csharp
var product = await repository.ById(id).AsReadOnly().AsEntityAsync(cancellationToken);
```

All three names can be configured. Passing `null` suppresses that individual method. Names must be
valid supported C# identifiers, distinct, and not collide with selector or configured projection
members; invalid configurations fail during model validation before files are written.

To omit every control explicitly:

```csharp
modelBuilder.Configure(options => options.GenerateTrackingMethods(false));
```

Method exposure does not alter query behavior. `UseDefaultTracking(...)` still configures selector
roots, and `.AsReadOnly()`, `.AsNoTracking()`, `.AsTracking()`, and identity-resolution policies in
the designer still execute even when their runtime counterparts are hidden or renamed. The
designer's own API names do not change when generated method names change.

`UseTrackingMethodNames(...)` does not implicitly re-enable methods. Apply presets first, then
enable individual extras. Presets reset the generation switch but preserve the chosen names and
tracking defaults. The JSON manifest records both defaults, the generation switch, and emitted
method names.

Hiding these conveniences is not a security boundary: legacy selectors still accept entities and
have an advanced `ConfigureQuery` escape hatch. For genuinely EF-free service contracts, choose
closed-operation mode. Optional [CQRS selectors](CQRS-SELECTORS.md) instead have read-only
capability classes, no public query hook, and no-tracking read defaults. The same tracking
exposure/name settings apply there without changing the read selector's capability type.

## Generation scope and examples

These options configure the design-time tool, through a designer; no new CLI flag is needed.
They do not reconfigure the independent attribute/source-generator route. Existing selectors
cannot silently ignore a renamed/hidden API: request `--generate-selectors` to regenerate the
tool-owned surface. If those types are attribute-generated, remove `GenerateSelectors` and rebuild
before switching to the designer-generated surface, so two generators do not define the same types.

The [configuration examples](../../../samples/ConfigurationExamples/README.md) show all three choices:
the simple example hides controls, the configured catalog exposes only `AsReadOnly()`, and reporting
uses EF-free read contracts with automatic no-tracking and no service-side controls.
