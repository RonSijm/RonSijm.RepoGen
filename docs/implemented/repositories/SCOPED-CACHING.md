# Closed-operation caching and visibility

Caching is off unless enabled and declared on a query. The closed-operation cache contract lives in
`RonSijm.RepoGen.Abstractions`; application ports remain EF-free.

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Contracts")
    .UseAsyncContextLeases()
    .UseClosedCaching());

entity.HasQuery("GetNames")
    .Cache(cache => cache.For(TimeSpan.FromMinutes(5)))
    .Select(entry => entry.Name).List();
```

Provide one application adapter implementing `IRepositoryResultCache`, not a handwritten decorator
per repository. The generated operation supplies the key, dependency tags, expiration options,
factory and cancellation token. Streaming queries cannot be cached.

Use `UseClosedCaching(false)` (or `GenerateCacheDecorator(false)`) to disable it without rewriting
query declarations. The generated constructor then requires no cache adapter and no scope partition.

## Scope comes from the context actually acquired

The provider returns `DbContextLease.Owned(context, scopeKey)` or
`DbContextLease.Borrowed(context, scopeKey)`. The key is an opaque application-owned partition:
include database identity and user/role/visibility identity whenever those change results. RepoGen
does not inspect connection strings, infer tenancy, or treat a scope as authorization.

Missing lease scope fails clearly. For an explicitly single-context application, use
`UseClosedCaching("my-single-database-and-visibility-scope")`. Do not reuse that fixed scope across
different databases/users. Request keys select contexts; they are not automatically trusted cache
partitions. A keyed unit of work binds its first key and rejects attempts to change it.

Generated keys include operation shape and serialized arguments. Scope hashes partition both keys
and tags; equal arguments in different scopes cannot share entries or invalidation namespaces.
Opaque provider implementation changes cannot be inferred from a lambda model: use an application
scope/version deployment policy or flush cache when changing registered query code.

Root tables, explicit dependencies, typed multi-source roots and discovered navigation dependencies
participate in tags. Predicate/sort/dictionary/scalar/cursor access and inspectable transitive EF
global-filter dependencies are included, not only returned DTO members. Constant-name EF.Property
navigations and normalized query-filter entity roots are inspected without evaluating getters.
For opaque helpers and registered SQL/provider roots, declare every additional table with
`DependsOn<OtherEntity>()`. The owner tag is always included. Writes collect all pending changed
tracked types, not only the entity named by the method.

## Transaction visibility and failures

Cached reads bypass the adapter while the context has pending tracked changes or an explicit/ambient
transaction. The SQL query still runs; bypass does not synthesize a view of unsaved tracker changes.

With `RepositoryUnitOfWork`/`KeyedRepositoryUnitOfWork`, generated operations register one cache
commit observer per adapter. Stage does not invalidate. Save outside a transaction invalidates
after persistence; writes inside the work's explicit transaction invalidate after commit, not save.
Rollback does not invalidate, and a rolled-back work is terminal: create a fresh scope.

Without a unit of work, successfully persisted commands invalidate immediately. A saved-result
mapping failure also invalidates: failing to produce a DTO must not retain a stale cached read.
Cached writes inside an unmanaged transaction are rejected before mutation, because RepoGen cannot
know when that transaction commits. Use the unit-of-work transaction boundary.

Failed after-commit invalidation throws a distinct `RepositoryCommitObserverException`. Data may
already be committed; do not retry the write. Durable recovery/outbox delivery is an application
integration concern, not a false guarantee of atomic database-plus-cache persistence. Writes made
outside generated operations must use shared save/commit observers or explicitly invalidate tags.

## Optional memory adapter

`RonSijm.RepoGen.Caching` provides `MemoryRepositoryResultCache` over an application-owned
`IMemoryCache`. Register the adapter as a singleton so tag generations are shared across requests:

```csharp
services.AddMemoryCache();
services.AddSingleton<IRepositoryResultCache>(provider => new MemoryRepositoryResultCache(
    provider.GetRequiredService<IMemoryCache>()));
```

By default it stores JSON snapshots, so mutating a returned collection cannot poison the next cache
hit. DTOs must be JSON-serializable; supply serializer options where needed. Set `cloneResults: false`
only for results whose immutability you control. Without explicit expiration it uses five minutes.

Tag generations prevent an invalidated in-flight fill being reused. Concurrent misses can still
execute multiple factories; this is not a stampede lock. Entry size is one, allowing a caller-supplied
memory cache size limit. Tag-generation bookkeeping grows with encountered scope/tag combinations;
applications with unbounded user scopes need their own bounded generation strategy/adapter.

An `IDistributedCache` consumer can implement the same seam using its chosen backend. A correct
distributed tag adapter needs coordinated generation/version storage and race-safe invalidation;
bare get/set/remove cannot promise that by itself. RepoGen deliberately supplies no pretend-atomic
distributed implementation.

The older selector decorator remains a compatibility path with its original single-context/transaction
limitations. It is not silently upgraded into scope-aware transactional caching.

## Required visibility policies

```csharp
entity.HasVisibilityPolicy("Live", entry => !entry.IsDeleted);
entity.HasQuery("AdministrativeHistory").BypassVisibility("Live")
    .Select(entry => entry.Name).List();
```

Policies apply to repository roots/lookups, named reads and command selection. `From` applies each
configured source entity's policies, even when that source has no generated repository. Bypass is
a reviewed designer operation, never an arbitrary service flag. Names must be declared and unambiguous.
EF global filters remain active; bypassing a RepoGen policy does not call `IgnoreQueryFilters`.

Policies do not automatically filter every navigation or validate created payloads. Explicitly put
navigation/creation invariants in the relevant projection/loading/mapping/authorization boundary.
Registered providers own their root policies and cannot pretend RepoGen applied hidden predicates.
The optional [navigation review guard](../queries/NAVIGATION-VISIBILITY.md) flags inspectable unreviewed
navigation access and inventories designer receipts; it does not rewrite navigation queries.

See the [Ledger example](../../../samples/ConfigurationExamples/Ledger/Program.cs), which verifies database
partitioning, saved invalidation, transaction bypass and rollback through EF-free read/write ports.
