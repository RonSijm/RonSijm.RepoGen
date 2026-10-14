# Generation options: start simple, opt in deliberately

RepoGen does not require a designer, caching provider, tenant resolver, async context factory,
CQRS contract project, or unit-of-work service for ordinary CRUD. A generated repository can take
your existing scoped `DbContext` directly.

However, the old compatibility defaults generate several optional helper APIs. Not configuring
an advanced query is different from suppressing every advanced helper in the source file.

## Choose a starting point

For a small setup without a designer:

```powershell
repogen generate --project path/to/My.Database.csproj --generate-selectors --profile simple
```

In a designer, use the equivalent preset before individual overrides:

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults());
```

Both retain key/index lookups, collection reads, single-entity writes, fluent staged writes,
`SaveChanges` and `SaveChangesAsync`, and direct context injection. The simple preset omits the
generated cache decorator, range operations, retry overload, transaction wrappers, generic
set-based selector operations, and runtime tracking controls. Basic synchronous staging such as
`Add(entity)` remains available. Designer tracking policies still apply.

Leaving `--profile` out preserves existing compatibility defaults. `--profile compatibility`
and `UseCompatibilityDefaults()` explicitly select that same preset. Existing consumers do not
silently lose generated methods during regeneration.

| Generation setting | Compatibility / no preset | Simple | Explicit override |
| --- | --- | --- | --- |
| Context injection | Direct, borrowed context | Direct, borrowed context | `UseDbContextResolver()`; closed mode can use `UseAsyncContextLeases()` |
| Cache decorator/provider interface | Generated | Omitted | `GenerateCacheDecorator(bool)` |
| Range writes | Generated | Omitted | `GenerateBatchOperations(bool)` |
| Concurrency-retry save overload | Generated | Omitted | `GenerateConcurrencyRetry(bool)` |
| Repository transaction wrappers | Generated | Omitted | `GenerateTransactions(bool)` |
| Generic set-based selector writes | Omitted | Omitted | `GenerateSetBasedOperations(bool)` |
| Synchronous helper methods | Generated | Generated | `GenerateSynchronousMethods(bool)` |
| Selector tracking controls | Existing `AsNoTracking` / `AsTracking` / identity-resolution names | Omitted | `GenerateTrackingMethods(bool)`; `UseTrackingMethodNames(...)` |
| Separate closed read/write contracts | Off | Off | `UseClosedRepositories("Namespace")` |
| Separate scripted test doubles | Off | Off | `UseScriptedTestDoubles("Testing.Namespace")` in closed mode; explicit test output/runtime; per-entity `GenerateTestDouble(bool?)` |
| Shared typed test stores | Off | Off | `UseInMemoryTestStores(bool)` plus selected `HasInMemoryStore(...)`; seeds/copy/baselines only, no automatic operation execution |
| Portable automatic test reads | Off | Off | `UseInMemoryTestQueries(bool)` requires stores; optional `RequirePortableTestQueries(bool)` and query `RequireTestOverride(reason)` |
| Fixture scalar-call translations | None | None | Model `HasInMemoryTranslation(name, call, replacement)` applies to enabled automatic reads; [exact typed calls](../testing/INMEMORY-TRANSLATIONS.md), explicit fixture semantics, unchanged production; no inferred provider guarantees |
| Command scalar-call translations | Off | Off | Per-command `UseInMemoryTranslations(bool)` requires automatic commands and model registrations; [selection/setters/batches/collections/saved results](../testing/INMEMORY-COMMAND-TRANSLATIONS.md), existing structural/permission guards, no store/value-adapter rewrites |
| Tracked fixture scalar equality | Ordinary structural | Ordinary structural | Store-scoped `HasInMemoryValueEquality(property, equality)` / `WithoutInMemoryValueEquality(property)`; [state/token comparison](../testing/INMEMORY-VALUE-EQUALITY.md) for ordinary non-identity scalars; strict round trips and query/key/provider semantics unchanged |
| Stateful tracked test commands | Off | Off | `UseInMemoryTestCommands(bool)` requires stores; independent `RequirePortableTestCommands(bool)` and command `RequireTestOverride(reason)`; scalar writes and [bounded root-row batches](../testing/INMEMORY-BATCHES.md), shared Stage/Save/rollback; no value generation |
| Immediate scalar test commands | Off | Off | Per-command `SimulateImmediateWrites(bool)` additionally requires automatic test commands and eligible stores; Update/Delete, counts/outcomes/returned rows, saved-only execution; [no SQL/race guarantees](../testing/INMEMORY-IMMEDIATE-WRITES.md) |
| Insert/upsert scalar test commands | Off | Off | Same per-command opt-in for InsertIfAbsent/InsertOrUpdate; supplied scalar inputs/explicit insert factories, all-saved-row conflict probe and update-only visibility; [no SQL sequence/locking guarantees](../testing/INMEMORY-ATOMIC-WRITES.md) |
| Bounded child-reconciliation test commands | Off | Off | Per-command `SimulateCollectionWrites(bool)` requires automatic commands and selected writable parent/child stores; designed ownership/scalar or [composite matching](../writes/COMPOSITE-COLLECTION-KEYS.md) policies, Stage/Save/rollback; [no inferred graph persistence](../testing/INMEMORY-COLLECTIONS.md) |
| Fresh saved command results | Off | Off | Per-command `ReturnsSaved(...)` on tracked Create/Patch or bounded CreateMany/PatchMany + Save; complete-key untracked projection query, batch values in input order; fixture navigation results additionally require automatic reads and declared relationships; [separate read, not atomic returning](../writes/SAVED-COMMAND-RESULTS.md) |
| Portable test navigation loading | None | None | Per-entity `HasInMemoryNavigation(member)` requires automatic reads and selected public entity-shaped stores at both ends; `WithoutInMemoryNavigation(member)` removes one edge |
| Separate selector read/write ports | Off | Off | `UseCqrsSelectors(bool)`; alternative to closed mode |
| Native update/delete-and-return writer | Off | Off | `UseReturningWrites<TWriter>(bool)` in closed mode; only declared `ReturnsMany` commands inject it |
| Data-contract checks in optional API snapshots | Off | Off | `IncludeApiDataContracts(bool)` in closed mode; output remains an explicit runner choice |
| Entity selection | All eligible EF entities | All eligible EF entities | `GenerateOnlyConfiguredEntities()` and per-entity `RepositoryEnabled` |
| Generated persistence namespace | Existing entity/projection placement; shared RepoGen helpers | Same | `UseGeneratedNamespace("Namespace")`; null restores default placement |
| Navigation visibility review guard | Off | Off | `RequireNavigationVisibilityReview(bool)`; nullable overrides and policy/reason receipts in designer scopes |

These are generation switches, not runtime feature flags. Changing them requires regenerating
and rebuilding. Generating a cache API does not automatically cache ordinary reads or require a
cache provider to construct the undecorated repository.
The helper rows describe selector-mode output. Closed-operation output is instead determined by
its configured named queries/commands and capabilities; it does not generate the legacy CRUD
helper surface simply because one of those switches is true.

The CLI preset runs first; explicit designer configuration runs afterward. Within a designer,
calls run in order, so apply a preset **before** enabling extras:

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .GenerateBatchOperations()
    .GenerateCacheDecorator());
```

Presets reset context-resolution/lease choices and the helper switches in the table. They do not
erase the contract/generated/testing namespace, explicit in-memory store/read/strict settings or CQRS-selector choice, navigation-review settings, entity-selection settings, tracking policies/method names, or
configured entity operations/data-contract snapshot choice or explicitly selected atomic/returning writers. They do reset whether tracking methods are generated. In particular,
calling a preset after `UseAsyncContextLeases()` disables those leases.

## What is opt-in at entity/query level?

Scripted testing output is separately opt-in via `UseScriptedTestDoubles(namespace)` and nullable
entity `GenerateTestDouble(bool?)` overrides. Closed port signatures are reused, with typed explicit
arrangements rather than query/write execution. Output and runtime dependencies belong in test
projects only. See [scripted test doubles](../testing/SCRIPTED-TEST-DOUBLES.md) and the focused
[ScriptedPorts](../../../samples/ConfigurationExamples/ScriptedPorts/README.md)/
[ScriptedStreams](../../../samples/ConfigurationExamples/ScriptedStreams/README.md) designers.

Shared fixture storage is independently opt-in via `UseInMemoryTestStores()` and per-entity
`HasInMemoryStore<TKey>(key, copy)` or an explicit public `HasInMemoryStore<TData, TKey>(key, copy)`.
Disabled declarations add no storage infrastructure; `WithoutInMemoryStore()` removes one store.
Selected scripts share a scoped fixture, optionally partitioned by the explicitly configured context
key. Baseline reset/copy isolation is arrangement infrastructure, not query execution or Save/rollback.
See [in-memory fixtures](../testing/INMEMORY-FIXTURES.md), [MemoryFixtures](../../../samples/ConfigurationExamples/MemoryFixtures/README.md)
and [MemoryPartitions](../../../samples/ConfigurationExamples/MemoryPartitions/README.md).

Automatic fixture reads are separately opt-in via `UseInMemoryTestQueries()`. It reuses portable
designer criteria, projections, visibility, terminals and typed multi-source joins/subqueries/set
operations; scripts still override any read. Participating sources require selected entity-shaped
or explicitly mapped public stores and are captured together in one designed fixture partition.
Per-entity `UseInMemoryModelQueryFilter()` additionally reuses portable scalar filters from the final
EF model on row-only read roots, including each `From` dataset. It is off by default; ignored/absent
filters add no output. Context-captured filters, filtered navigation graphs, AutoInclude and writes
retain explicit overrides. Model-filter and designer-visibility bypasses stay independent. See
[model filter semantics](../testing/INMEMORY-MODEL-FILTERS.md) and
[MemoryModelFilters](../../../samples/ConfigurationExamples/MemoryModelFilters/README.md).
The additional `includeNavigations: true` argument is independently off by default. Every filtered
participant must opt in before declared collection/optional-reference loading; required filtered
references, filtered many-to-many and unsafe null access retain overrides. Writes remain separate.
See [filtered navigation semantics](../testing/INMEMORY-FILTERED-NAVIGATION.md) and
[MemoryFilteredNavigation](../../../samples/ConfigurationExamples/MemoryFilteredNavigation/README.md).
Named-query `IgnoreAutoIncludes()` independently disables user-configured model automatic loading
in production; it defaults to off and preserves explicit paths, profiles and projection inference.
Suitable fixture reads then need not simulate that disabled policy. Owned loading, filters and
commands stay separate. See [loading policies](../queries/EXPLICIT-LOADING-POLICIES.md) and
[MemoryLoadingPolicies](../../../samples/ConfigurationExamples/MemoryLoadingPolicies/README.md).
`MapInMemoryEntity<TData>(initializer)` opts into scalar proxy reads; `WithoutInMemoryEntityMapping()`
removes that map and any dependent write/value adapters. Helpers are omitted with automatic reads
disabled unless separate mapped writes need their scalar slots. This changes no
production query/API/cache shape. See [mapping boundaries](../testing/INMEMORY-PROXIES.md) and
[MemoryProxies](../../../samples/ConfigurationExamples/MemoryProxies/README.md). No extra join flag is required.
`HasInMemoryNavigation(member)` optionally loads accessed references/collections and unfiltered
profiles by the EF relationship keys, over one shared snapshot. No related visibility policy or inverse
is inferred. See [navigation boundaries](../testing/INMEMORY-NAVIGATION.md) and
[MemoryNavigation](../../../samples/ConfigurationExamples/MemoryNavigation/README.md).
Filtered/provider loading, arbitrary proxy adaptation and fake writes are not inferred. Optional
`RequirePortableTestQueries()` rejects unacknowledged non-portable reads at generation time, and
`RequireTestOverride(reason)` documents one deliberate script-only query without changing production.
Read generation alone does not simulate writes/Stage/Save. See [portable fixture reads](../testing/INMEMORY-QUERIES.md),
[MemoryCriteria](../../../samples/ConfigurationExamples/MemoryCriteria/README.md) and
[MemoryCoverage](../../../samples/ConfigurationExamples/MemoryCoverage/README.md).

Automatic tracked fixture writes are independently opt-in via `UseInMemoryTestCommands()`.
Per-command `UseInMemoryModelQueryFilter()` additionally reuses portable final-model scalar filters
for tracked selection/batches, non-returning immediate counts and fresh scalar/DTO saved reads.
It is off independently of entity read settings. Inserts/shared Save are not filtered; context,
graph/native-returning/upsert behavior still requires overrides. See
[filtered command semantics](../testing/INMEMORY-MODEL-WRITE-FILTERS.md) and
[MemoryModelWrites](../../../samples/ConfigurationExamples/MemoryModelWrites/README.md).
Per-command `includeNavigations: true` is additionally off by default. It permits ordinary filtered
saved DTO graphs, not graph mutations. The command owns its root permission; other filtered
datasets need entity graph permission. Scalar results add no graph infrastructure. See
[saved filter semantics](../testing/INMEMORY-SAVED-FILTERS.md) and
[MemorySavedFilters](../../../samples/ConfigurationExamples/MemorySavedFilters/README.md).
The same two existing command opt-ins additionally support portable model filters for scalar
native returning Update/Delete. No extra feature switch or read backend is required. Returning
capability/write-map checks remain; results are matched changed/deleted values, not ReturnsSaved.
See [native filter semantics](../testing/INMEMORY-NATIVE-FILTERS.md) and
[MemoryNativeFilters](../../../samples/ConfigurationExamples/MemoryNativeFilters/README.md).
The same designed scalar Create/Patch/Delete and bounded PatchMany/leaf DeleteMany operations
execute against pending/saved fixture state, sharing Stage/Save and checkpoint rollback. No key,
computed-value or rowversion generation is inferred. `RequirePortableTestCommands()` rejects
unacknowledged unsupported commands; command `RequireTestOverride(reason)` documents a required
script. See [fixture writes](../testing/INMEMORY-WRITES.md), [fixture batches](../testing/INMEMORY-BATCHES.md),
[MemorySharedWrites](../../../samples/ConfigurationExamples/MemorySharedWrites/README.md) and
[MemoryBatches](../../../samples/ConfigurationExamples/MemoryBatches/README.md).

Immediate scalar Update/Delete and native-returning/consume command definitions can separately opt into
`SimulateImmediateWrites()` on each command. This never changes production contracts/provider selection
or flushes/refreshes tracked pending fixture rows. `false` removes the opt-in, and required typed overrides
still take precedence. Suitable scalar InsertIfAbsent/InsertOrUpdate also use this explicit opt-in,
with declared all-saved-row conflict targets and update-only visibility/expected versions. Graph/cascade
behavior and SQL/provider guarantees remain excluded. See [insert/upsert fixture semantics](../testing/INMEMORY-ATOMIC-WRITES.md),
[immediate fixture semantics](../testing/INMEMORY-IMMEDIATE-WRITES.md) and the
[MemoryImmediateWrites designer](../../../samples/ConfigurationExamples/MemoryImmediateWrites/README.md).
The focused [MemoryAtomicWrites designer](../../../samples/ConfigurationExamples/MemoryAtomicWrites/README.md)
demonstrates separate insert/existing/update outcomes without adding EF/provider dependencies to its consumer.

With automatic fixture commands selected, `HasInMemoryGeneratedValue(property, factory)` explicitly
supplies suitable mapped ID/default/computed/token values. EF generation metadata selects insert/update
timing, but SQL expressions are never translated into factories. Invalid adapters fail with `RGD3501`;
`WithoutInMemoryGeneratedValue(property)` removes one and replacing/removing the store clears them.
No adapter or extra infrastructure is enabled by default. See [generated-value policies](../testing/INMEMORY-GENERATED-VALUES.md)
and the [MemoryGeneratedValues designer](../../../samples/ConfigurationExamples/MemoryGeneratedValues/README.md).

Existing `MapInMemoryEntity` scalar declarations and `HasInMemoryNavigation` declarations can be
combined for [related public fixture shapes](../testing/INMEMORY-PROXY-NAVIGATION.md), without constructing
the persistence model. This adds no global flag: automatic reads, suitable mapped stores and selected
edges must all be explicit. Missing related scalars/keys retain strict coverage diagnostics. See
[MemoryProxyNavigation](../../../samples/ConfigurationExamples/MemoryProxyNavigation/README.md).

The same existing declarations also cover [required many-to-many reads](../testing/INMEMORY-MANY-TO-MANY.md)
through explicitly selected CLR join stores. Both endpoint stores and the join keys must be
entity-shaped or fully scalar-mapped. [Nullable links](../testing/INMEMORY-OPTIONAL-LINKS.md) also work for
optional source FKs and explicitly non-null-filtered optional targets. Implicit/shared joins and
unguarded target null propagation/loading retain explicit override coverage.
There is no extra global flag or production change. See
[MemoryManyToMany](../../../samples/ConfigurationExamples/MemoryManyToMany/README.md).
[MemoryOptionalLinks](../../../samples/ConfigurationExamples/MemoryOptionalLinks/README.md) shows directional
nullable keys and shared designer guards with no additional switch or consumer-side query API.
`HasInMemoryNavigation(navigation, preserveMissingTargets: true)` is independently off by default.
It admits per-link null slots on an optional-target skip navigation only for explicitly null-safe
scalar/DTO query pipelines. It changes no production query, contract or dependency and does not
authorize SQL propagation, entity identity or explicit loading. See
[MemoryNullTargets](../../../samples/ConfigurationExamples/MemoryNullTargets/README.md) and
[null-preserving boundaries](../testing/INMEMORY-NULL-TARGETS.md).

`HasInMemoryRelationshipComparer(principalKeyProperty, comparerFactory)` independently configures
one principal-key component for declared fixture-read references/collections and explicit CLR joins.
It defaults to absent, applies in both directions and retains complete-key uniqueness/null rules.
Query predicates, store identity, tracked writes and production equality are unchanged; compatible
CLR comparer metadata can be acknowledged, not converters/provider comparers. See
[MemoryRelationshipComparers](../../../samples/ConfigurationExamples/MemoryRelationshipComparers/README.md)
and [relationship comparison boundaries](../testing/INMEMORY-RELATIONSHIP-COMPARERS.md).

`HasInMemoryProjectionTranslation` is independently opt-in and retains scalar inputs with explicit
flat public DTO construction. Automatic fixture reads can reuse the original named projection;
commands still require UseInMemoryTranslations. Production helper calls, SQL translation rules,
contracts and scalar writes remain unchanged. See [MemoryProjectionHelpers](../../../samples/ConfigurationExamples/MemoryProjectionHelpers/README.md)
and [DTO translation boundaries](../testing/INMEMORY-PROJECTION-TRANSLATIONS.md).

[ReconcileLinks](../writes/LINK-RECONCILIATION.md) separately declares bounded association-row writes. No
global feature flag is required: named commands work with simple defaults, while fixture execution
still needs stores/automatic commands and per-command SimulateCollectionWrites. Add/Update and
Delete-missing remain explicit; defaults keep missing rows and never persist endpoint graphs. See
[MemoryLinks](../../../samples/ConfigurationExamples/MemoryLinks/README.md).
Link-builder `AllowSharedReferenceColumns(bool)` is independently off by default. Compatible
owner/target FK overlaps require complete target matching and typed owner-consistency checks;
it does not enable fixtures or loosen any update permission. See
[MemorySharedLinks](../../../samples/ConfigurationExamples/MemorySharedLinks/README.md) and
[shared-reference boundaries](../writes/SHARED-REFERENCE-COLUMNS.md).
TargetReference / AdditionalReference are independently unconfigured. They explicitly account for
every required FK of a multi-reference join and grant complete scalar initialization on new links
only; they do not enable fixtures or permit matched-FK/endpoint updates. See
[MemoryAdditionalReferences](../../../samples/ConfigurationExamples/MemoryAdditionalReferences/README.md)
and [named-reference boundaries](../writes/ADDITIONAL-LINK-REFERENCES.md).
OptionalAdditionalReference is separately unconfigured. It explicitly admits a nullable additional
FK with complete Add-only all-null/all-present values, rejects partial composite keys and preserves
matched presence/values. Owner and matching target remain required; no fixture or navigation-read
option is enabled implicitly. See [MemoryOptionalReferences](../../../samples/ConfigurationExamples/MemoryOptionalReferences/README.md)
and [optional-reference boundaries](../writes/OPTIONAL-ADDITIONAL-REFERENCES.md).

`MapInMemoryWrites<TData>((candidate, saved) => ...)` independently declares scalar write adaptation
for public fixture rows backed by internal models. Full scalar/key coverage and a matching read map
are required; reverse DTO mappings and entity constructor defaults are never inferred. No new global
flag is needed. `WithoutInMemoryWrites()` removes the write/value adapters but retains reads/storage/scripts.
Automatic commands or entity writes disabled emit no adapter/value registrations. Suitable explicit
generated-value factories work through the mapped view too. See [write mapping rules](../testing/INMEMORY-PROXY-WRITES.md)
and [MemoryProxyWrites](../../../samples/ConfigurationExamples/MemoryProxyWrites/README.md).

`HasInMemoryKeyComparer<TKey>(() => ...)` optionally configures an existing fixture store's key
identity. Omitting it retains `EqualityComparer<TKey>.Default` with no extra generated argument or
receipt. It affects arrangement/pending/saved key dictionaries only—not predicates, relationship
matching, batch input matching, context-partition comparison or production collation. Factory expressions
are evaluated once per store/partition, not invoked at generation. `WithoutInMemoryKeyComparer()`
retains other adapters; replacing/removing the store clears the setting. See [comparer rules](../testing/INMEMORY-KEY-COMPARERS.md)
and [MemoryKeyIdentity](../../../samples/ConfigurationExamples/MemoryKeyIdentity/README.md).

The optional [contract runner and relational fixture harness](../testing/REPOSITORY-CONTRACT-FIXTURES.md)
are testing-package choices, not generation flags. They do not alter the designer, ordinary outputs
or dependencies. Tests explicitly configure provider/production registrations and owned database
setup/reset. The ordinary Testing package remains EF-free; the real-provider harness is separate.

- `HasAccessor`, `HasFilter`, `HasProjection`, `HasFragment`, `HasQuery`, and `HasCommand` add only
  the operations you declare, beyond automatic key/index lookups and the common selector API.
- `ProtectWrite(property)` optionally guards ordinary scalar assignments in closed repositories.
  Permissions must be declared locally on each command/child mapping; false removes the guard.
  It adds no runtime infrastructure, audit convention or selector permission flags. See
  [write protection](../writes/WRITE-PROTECTION.md).
- `HasSortableFields(...)` and query `AllowSorting(...)` generate field-name whitelists. Without
  a whitelist, arbitrary string-based sorting is not enabled.
- Default sorting is absent unless configured. `GetAllUnordered()` and query
  `UseDefaultSort(false)` bypass a configured default; paged results should use a stable ordering.
- Includes, split queries, query tags, cursor paging, grouping, result pipelines, and cache
  policies are configured per operation. Configuring a grouped report does not add one to every
  other entity.
- `HasQuery(...).From(...)` opts into typed multi-source joins/subqueries/set operations for that
  named query only. Ordinary roots are unchanged; no extra CLI flag is needed. See
  [Multi-source queries](../queries/MULTI-SOURCE-QUERIES.md).
- `HasQuery(...).OnWriteRepository()` optionally places a named read on the write capability
  in CQRS/closed mode. The default remains the read port; false restores that default. Command
  reads use the ordinary tracking policy and cannot enable caching. No new infrastructure or
  service-side switch is introduced. See [Command-side queries](../queries/COMMAND-SIDE-QUERIES.md).
- `ProjectionEnabled = false` removes consumer-defined projection entry points for that entity;
  reviewed designer projections and named queries remain available.
- `RequireNavigationVisibilityReview()` optionally rejects unreviewed navigation access to targets
  with configured visibility policies. Receipts explain actual filtering or deliberate differences;
  they do not inject predicates or perform authorization. No runtime controls are added. See
  [Navigation visibility](../queries/NAVIGATION-VISIBILITY.md).
- `WhereIn(...)` / `WhereNotIn(...)` add request membership only to that named query/result stage.
  Null rejection and empty-set semantics are the defaults. Optional null/empty overrides,
  deduplication and raw-input bounds are design-time settings, with no service-side or CLI flags.
  Unconfigured queries generate no membership preparation. See [Membership queries](../queries/MEMBERSHIP-QUERIES.md).
- `Load(...)`, `HasLoadProfile(...)`, and `UseLoadProfile(...)` configure typed navigation chains
  only where explicitly attached. Registering an unused profile changes no default query.
  Request-bound loading is supported on named operations. `RequireExplicitLoadStrategy()` is an
  opt-in multiple-collection guard, not an automatic split-query policy.
- Projection `AutoIncludeNavigations(false)` suppresses automatic root-navigation includes for
  that mapping; explicit paths/profiles remain active. Automatic detection stays on by default
  for compatibility. See [Loading profiles](../queries/LOADING-PROFILES.md) for SQL projection versus
  entity-graph semantics.
- `RepositoryEnabled = false` excludes an entity entirely. `Exclude(property)` suppresses its
  automatic accessor, not the EF model's property or its use in an explicitly configured query.
- In closed-operation or CQRS-selector mode, `ReadEnabled` / `WriteEnabled` and `ExcludeQuery` /
  `ExcludeCommand` control the selected public capabilities. Compatibility selector mode still
  rejects read/write capability switches. Application-port binding remains closed-only.
- A reviewed `HasCommand(...).Update(...)` / `.Delete()` is explicitly opted into by
  its declaration. `GenerateSetBasedOperations(false)` suppresses generic selector escape
  hatches, not these named commands.
- `CreateMany` / `PatchMany` / `DeleteMany` optionally declare bounded tracked batch commands in closed mode.
  Limits, per-item mappings/versions and Save/Stage are designer choices; no methods or provider
  dependencies are added without a declaration. The range-helper switch does not remove named
  commands. See [matching-key batches](../writes/BATCH-COMMANDS.md) and [bounded creation](../writes/CREATE-MANY-COMMANDS.md).
- Optional closed-operation SQL timeouts inherit from model to entity to named query/command.
  Result-builder settings apply to the whole query. Explicit null retains the context setting;
  `InheritCommandTimeout()` removes only that override. No runtime service controls or extra
  lifetime dependencies are added. See [Command timeouts](../repositories/COMMAND-TIMEOUTS.md).
- Native `Update(...).ReturnsMany(...)` / `Delete().ReturnsMany(...)` requires a selected provider
  writer and adds only the named returning operations. It remains off by default and does not
  require async leases, caching, tenancy or a unit of work. See [Returning writes](../writes/RETURNING-WRITES.md).

Tracking behavior is unchanged by the presets: compatibility/write selectors inherit the context policy unless
you configure `UseDefaultTracking(...)` or a query's `AsReadOnly()` / `AsTracking()`. Closed
and CQRS-selector `IRead…Repository` operations instead default to no-tracking, independently of write
defaults. Use `UseReadOnlyTracking(...)` or an explicit query policy to override that default.
See [Tracking policies and API names](../repositories/TRACKING-POLICIES.md). Retry helpers do not invent a
conflict-resolution policy; callers must supply one.

## What is not separately switchable yet?

The shared selector core still includes fluent filtering, ordering, loading, paging,
grouping/aggregates, and terminal read helpers. There is not a flag for pruning each of those
methods individually. The simple preset makes setup and infrastructure simple; it is not a
promise that the generated source contains only five CRUD methods.

The presets apply to the design-time **tool**. The separate attribute/source-generator route
retains its existing shared runtime selector surface.

Closed-operation mode is the option for a deliberately narrow service API: consumers see only
named configured operations, not selector-building methods. EF stays in the persistence output.
`UseCqrsSelectors()` instead offers read/write ports with read-only selector types and configured
named operations, without separate EF-free contracts. See [CQRS selectors](../repositories/CQRS-SELECTORS.md).
Async leases are optional even in closed mode. The reusable unit-of-work runtime is separately
registered only when needed; `GenerateTransactions(false)` controls legacy generated wrappers,
not the existence of that separately requested runtime service. `UseClosedCaching()` is a separate
opt-in requiring an EF-free adapter and actual lease scope, or an explicitly fixed single-context
scope. See [Scoped caching](../repositories/SCOPED-CACHING.md).

Additional advanced options do nothing when absent:

| Option/declaration | Default | Enabled behavior |
| --- | --- | --- |
| `UseKeyedContextLeases<TKey>()` / `UseContextKey(...)` | Direct context or explicitly chosen non-keyed lease | Request-driven typed provider; shared work cannot switch keys |
| `UseClosedCaching(scope?)` + query `Cache(...)` | No closed cache dependency or reads | Scoped keys/tags; commit observer registration/invalidation |
| `HasVisibilityPolicy(...)` / `BypassVisibility(...)` | EF's own filters only | Required root predicates; trusted named designer bypass |
| `Create` / `CreateMany` / `Patch` / `TrackedDelete` | No named tracked commands | Explicit Save vs Stage, approved fields and optional saved DTO; CreateMany has a finite item limit |
| `ExpectVersion(...)` / `WithOutcome()` | Existing provider/write behavior | Original-version binding or conditional SQL outcome, no stale-client retry |
| `UseAtomicWrites<TWriter>()` | No atomic writer dependency | Selected provider capability for explicitly declared insert-if-absent/upsert |
| `FromProvider<...>(...)` | Ordinary typed EF roots | Registered infrastructure query/SQL/temporal root |
| Model `UseCommandTimeout(seconds)` | Context/provider timeout | Default SQL-command timeout for closed operations, restored afterward |
| Entity/query/command/result `UseCommandTimeout(seconds)` | Inherit the next level | Named closed-operation override; explicit null retains context settings, `InheritCommandTimeout()` resets inheritance |
| `Paged(Serializable)` | BestEffort, two queries | Stable read transaction spanning count/items |
| `RequirePagingOrder()` | Offset guard off; result cursor guard on | Generation error for missing explicit ordering; uniqueness still caller-designed |
| `Validate` / parameterized `HasFragment` | No extra guards/fragments | Reviewed structural validation before acquisition and reusable predicates |
| Result `OrderByIf` / `ThenByIf` and descending variants | No optional sort stages | Fixed designer ranking/tie expressions only on enabled branches, with fallback ordering checks |
| Result `SkipIf` / `TakeIf` / `DistinctIf` | No optional limit/de-duplication stages | Disabled counts are not evaluated; only explicitly configured query shapes change |
| Result `Dictionary(..., duplicateKeys)` | Throw on duplicate keys | Explicit `KeepFirst`/`KeepLast` policies require guaranteed query ordering; key/value projection and EF-free signatures stay the same |
| Result `OrderByField(...)` | No field-name result sorting | Typed request plus fixed designer aliases/keys; optional default-field or preserve-order behavior, with input validation before acquisition |
| Request-aware `Dictionary` / `Lookup` / `Min` / `Max` / `Sum` / `Average` | Existing row-only mappings | Fixed terminal expressions can share a typed input; no new runtime service or generated method unless configured |
| `CursorPaged<TCursor, TRequest>(...)` | Existing row-only cursors | Typed request in seek and/or cursor mapping; explicit ordering required, no inferred seek or runtime service |
| `ValidateCursor(...)` | No continuation guard | Cursor-only/request-aware structural checks for supplied cursors before acquisition/cache; first-page guard bodies are skipped |
| `AllowNullInput<T>(name)` | Expression-tree reference input non-null annotation | Deliberate outer nullable reference parameter; exact type/name validation |
| `InputNullability<T>(name, ...)` / `ResultNullability<T>(...)` | Existing non-null reference shapes | Explicit nested array/generic annotations; no new null/filter behavior or CLR overloads |
| `HasTypeAdapter(...)` / accessor `UseAdapter(...)` | Native property/input types and explicit projections | Reusable ID/request/DTO expressions; only explicitly selected properties change inferred inputs |
| Accessor `UseValueConverter<TProvider>()` | EF model CLR input type | Reverse the existing exact EF conversion for primary-key/index inputs; no EF model changes |
| `KeyNullability<TKey>(...)` | Non-null key and unannotated key children | Independent dictionary/lookup generic/array key annotations; nullable outer keys stay forbidden |
| Per-entity / per-contract output | Single file | Entity persistence plus optionally one generated CQRS interface per file |
| `WithApiSnapshot()` / `CreateApiSnapshot().CompareTo(...)` | No snapshot/baseline requirement | Reviewable candidate and source-contract changes; baseline acceptance remains explicit |
| Build checks | Disabled | Explicit no-write CI freshness and optional API compatibility check |
| In-memory C# compilation | Disabled | Explicit `ValidateCompilation(...)` from the optional Validation library; target references and compiler policy are caller-supplied |

Provider packages and the memory adapter are optional runtime references. The transaction runner is
explicitly called for replayable infrastructure work; enabling closed mode does not automatically
retry every command. Domain validation, authorization, audit conventions and distributed outbox
delivery remain consumer decisions.

## Runnable examples

UseInMemoryModelDefaults optionally reuses eligible constant insert defaults from the final model,
per writable fixture store. Off by default; replacement/removing writes clears it. Explicit factories
win, typed sentinels preserve supplied values, and seeds/updates do not rerun defaults. SQL/key/FK/
token/computed/conversion policies remain explicit. See [model defaults](../testing/INMEMORY-MODEL-DEFAULTS.md)
and [MemoryModelDefaults](../../../samples/ConfigurationExamples/MemoryModelDefaults/README.md).

Explicit nested annotations and projection reuse are described in [Contract nullability](../repositories/CONTRACT-NULLABILITY.md).

See [Configuration examples](../../../samples/ConfigurationExamples/README.md) for fifty-eight focused applications,
their designers, checked-in generated outputs, and a script that regenerates, compiles, and runs
all fifty-eight applications. The code-first runner references the Generation library used by the exe. Each manifest records the actual
feature switches, so differences are inspectable.

An optional `-ValidateCompilation` demo run checks C# against the applications' .NET 8 references
without changing generated output or adding application dependencies. Reference export is an
explicit tooling step, not another repository designer/runtime feature. See [Compiler validation](COMPILATION-VALIDATION.md).
