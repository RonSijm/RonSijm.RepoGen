# Implementation history and roadmap coverage

This is a cumulative delivery journal, not the current usage guide. Dated verification counts and
earlier descriptions belong to their historical slices. See [Implemented features](../implemented/README.md)
for available configuration, [Work in progress](../work-in-progress/README.md) for active unfinished
work, and [Future work](../future/README.md) for roadmap context.

Updated 2026-10-22. This tracks coverage of [the full feature plan](../future/REPOSITORY-FEATURE-PLAN.md), including the explicit optional/provider/application boundaries below. It does not promise every possible extension of the hypothetical plan. External target applications have not been changed by this implementation.

## Delivered foundation slice

- Opt-in closed-operation CQRS contracts: read, write, and combined interfaces; actual named instance methods; internal persistence selectors; shared scoped DI registrations.
- Separate EF-free contract output, with a dependency-free `RonSijm.RepoGen.Abstractions` project for page/cursor results and unit-of-work contracts.
- Existing application-port binding, including inherited methods, exact signature/nullability validation, and explicit bound-port DI registrations.
- Entity exclusions, configured-entity allowlisting, closed read/write capability switches, and query/command exclusions.
- Asynchronous owned/borrowed context leases, an EF async-factory adapter, typed keyed-provider interface, and a fixed-key adapter for per-request/unit-of-work selection.
- Shared context-scoped unit of work, explicit isolation/begin/commit/rollback, changed-tag collection, deferred after-commit observers, and distinct committed-but-observer/cleanup-failed exceptions.
- Explicit modular designer configuration, deterministic JSON operation manifests, and model/render validation without generated-file writes.
- Compatibility defaults remain unchanged; unsupported combinations produce diagnostics rather than silently changing lifetime/caching behavior.

Usage and limitations are documented in [Closed repositories](../implemented/repositories/CLOSED-REPOSITORIES.md).

## Delivered query-pipeline slice

- Ordered, typed result stages after entity/named projection or grouping: filtering, conditional filtering, sorting, projection, regrouping, distinct, and constant/request-driven limits.
- Anonymous intermediate projections and composite group keys; final closed-operation contracts remain named/public/EF-free.
- SQL-composed aggregate filters and grouped paging; group terminals no longer discard the configured group.
- Named scalar (`Min`/`Max`/`Sum`/`Average`), dictionary, and uncounted offset-slice terminals, alongside list/single/optional/count/exists/stream terminals.
- A neutral `SliceResult<T>` with next-offset metadata; one lookahead query, no count query.
- Shared request parameters across source/result filters, source/result sort keys, result projections, and limits; input-only `WhereIf` branches omit SQL predicates when disabled.
- Per-query default-sort control, stale/type-mismatched builder and ordering diagnostics, generated-local collision protection, and additive manifest stage inventory.
- Legacy selectors/cache branches and designer projections with ad-hoc projection disabled remain compatible. Runtime translation remains provider-owned; no client-filter fallback is inserted.

Examples and precise behavior are in [Result query pipelines](../implemented/queries/RESULT-QUERY-PIPELINES.md).

## Delivered configuration/examples slice

- A one-flag `--profile simple` CLI preset, usable without a designer, and equivalent `UseSimpleDefaults()` / explicit compatibility presets in the designer. Existing no-preset defaults remain unchanged; designer overrides run after CLI defaults.
- The simple preset removes generated cache, batch, retry, transaction, generic set-based selector helpers, and runtime tracking controls while keeping direct-context CRUD. Lifetime/contract/selection choices remain explicit; the remaining common fluent selector core is not individually pruned.
- Additive manifest feature switches, plus an option/default audit documenting what is selectable, always shared, or unsupported.
- Four initial checked-in, runnable SQLite examples with distinct tiny contexts: minimal-designer CRUD, an explicitly configured/cached catalog, closed read-only grouped reporting, and a deliberately loaded/filtered library graph with asynchronous context leases and an application-owned port. The current sixty-nine-example index groups them by topic; each example keeps its designer/model/contracts and guide beside its consumer, with separate linked-source build projects preserving bootstrap boundaries.
- An explicit regeneration/build/run script with separate model/designer projects, deterministic outputs, and no dependency on generated persistence code during first-generation bootstrap.

See [Generation options](../implemented/generation/GENERATION-OPTIONS.md) and [Configuration examples](../../samples/ConfigurationExamples/README.md).

## Delivered multi-source query slice

- Opt-in `HasQuery(...).From(...)` expression factories over typed `RepositoryQuerySources.Set<TEntity>()` roots: ordinary LINQ inner/left/composite joins, SelectMany, correlated existence/anti-joins, and set operations, without exposing a query builder to services or adding CLI flags.
- One captured context/lease for all sources; EF query-filter, tracking, splitting, and tag policies applied to the declared roots. Source sets need not have generated repositories or EF navigation relationships.
- Request-bound roots that reuse the same named/typed input across result stages, grouped HAVING/paging, dictionaries, and streaming.
- Inferred source/navigation dependencies, including later result-stage navigations, legacy cross-repository cache-tag invalidation, and deterministic source/operator manifest inventories.
- Diagnostics for opaque/client roots, captured designer state, unmapped/owned/shared sets, invalid contracts, stale builders, and misplaced entity clauses; safe emission of compiler-generated query-comprehension identifiers.
- The read-only reporting example now contains a left join to a second table and checks its nullable/missing-row behavior and operation context disposal.

See [Multi-source queries](../implemented/queries/MULTI-SOURCE-QUERIES.md). Expression-based operations still depend on provider translation; constructor DTOs should be projected after server-side filtering/sorting when their members cannot be translated.

## Delivered tracking-policy slice

- Designer-controlled generation of runtime tracking methods, configurable names on both single/collection selectors, and per-method omission with `null`. Compatibility names remain unchanged; the simple preset hides those controls.
- A neutral design-time `AsReadOnly(identityResolution: false)` policy. Named operations apply tracking internally, independently of whether public convenience methods are hidden or renamed.
- Closed read contracts default to no-tracking independently of selector/write/context defaults. `UseReadOnlyTracking(...)` and explicit query policies provide deliberate overrides, also across multi-source roots, pages, and streams.
- Validation for invalid/duplicate/conflicting method names and existing/attribute selector reuse; manifests expose effective tracking defaults and configured method names.
- Updated examples showing hidden controls, a single neutrally named catalog control, and EF-free reporting without consumer tracking APIs. No-tracking does not imply immutability or discard already-tracked pending writes.

See [Tracking policies](../implemented/repositories/TRACKING-POLICIES.md).

## Delivered navigation/loading slice

- Typed `Load(...).Include(...).ThenInclude(...)` chains for reference, collection, nested collection, owned, and many-to-many navigation paths, validated against the final EF model.
- Named, reusable `HasLoadProfile(...)` definitions attached explicitly to queries, projections, or entity defaults. Forward references resolve at design build time; registering an unused profile changes no query. No runtime profile API is added to services.
- Request-bound `Load<TRequest>` and `HasLoadProfile<TRequest>` with filtered collection continuations, sorting, and limits. The same request binds across root predicates and loading; unparameterized chain segments retain its type. Parameter-only loading inputs work with existing application ports.
- Opt-in `RequireExplicitLoadStrategy()` for multiple collection paths, conflicting-filter/stale-chain/missing-profile diagnostics, and per-projection `AutoIncludeNavigations(false)` without changing compatibility defaults.
- Nested loading and SQL-projection dependency inference for legacy cache tags, plus manifest profile/path/filter/strategy inventory. Entity defaults are not incorrectly applied to replacement From roots.
- The Library example returns a detached DTO graph through one EF-free read operation, with filtered nested notes/reviews and deliberate split loading. SQL-only projections still prune unused graph includes; no provider translation/client-filter fallback is introduced.

See [Loading profiles](../implemented/queries/LOADING-PROFILES.md).

## Delivered command/provider/tooling batch

- F09/F10: explicit tracked Create/Patch/Delete Save vs Stage, Optional presence/null patches, approved
  scalar mappings, generated/saved DTO results, original expected-version binding, honest conflict
  and conditional NotMatched outcomes, tracker restoration and committed-result failure semantics.
- F03/F07: designer request-to-key acquisition, stable keyed shared work, structural validation before
  acquisition, reusable parameterized predicates and request-bound group aggregate expressions.
- F04/F06: explicit replayable execution-strategy transactions with fresh owned contexts, operation
  timeouts, result-shape SQL cursor paging, stable counted-page transactions, optional ordering guards
  and named duplicate-preserving lookup terminals.
- F12: registered parameterized infrastructure query roots and optional SQL Server temporal roots,
  including actual relational temporal-period tests. Core does not take a SQL Server dependency.
- F11: optional native SQLite insert-if-absent/conditional upsert with generated returning values,
  unique-key/version races and a concurrency-safe tracked delete-and-return consume path. Unsupported
  provider/schema shapes are explicit; no unsafe fetch-then-insert fallback is called atomic.
- F13: actual-lease scope partitions, inferred/declared dependency tags, pending/transaction read
  bypass, after-commit invalidation, optional clone-safe memory adapter and trusted named visibility
  policies/bypasses. Closed caching can be disabled without retaining a provider/scope requirement.
- F02/F14: exact overloaded application-port binding, explicit outer nullable reference inputs,
  shared public generation engine, per-entity output, handwritten-output protection, obsolete/stale
  checks and an opt-in build/CI target. The code-first runner bootstraps without generated facades.
- A fifth runnable Ledger configuration demonstrates the advanced options together. The other four
  contexts still build/run with their smaller configurations and no new mandatory infrastructure.

Usage: [Typed commands](../implemented/writes/TYPED-COMMANDS.md), [Scoped caching](../implemented/repositories/SCOPED-CACHING.md),
[Optional providers](../implemented/repositories/PROVIDER-CAPABILITIES.md), [Code-first generation](../implemented/generation/CODE-FIRST-GENERATION.md).

## Roadmap coverage and remaining extensions

The subsequent provider/API-evolution batch adds:

- F10/F11: optional SQL Server atomic insertion with a locked declared unique-key probe and
  OUTPUT-generated values; conditional upserts and tracked binary rowversion patches/consume
  verified on actual SQL Server, including original versions held by both clients before saving.
- F14: per-generated-interface output, optional deterministic API snapshots, stable RGA compatibility
  diagnostics, caller-vs-implementer policies, and a no-write-before-breaking-change CLI gate.
  Approved baselines cannot overlap generated outputs and are never automatically accepted.
- Ledger now demonstrates the finer layout and a checked-in initial baseline; the simpler examples
  retain their original output layouts and have no baseline requirement or SQL Server dependency.

See [API compatibility](../implemented/generation/API-COMPATIBILITY.md) and [Optional providers](../implemented/repositories/PROVIDER-CAPABILITIES.md).

The contract-type fidelity batch adds:

- F02: optional input/result annotation trees for array elements and generic arguments, nullable
  outer references, saved values, final projected rows and dictionary/lookup values. Named projection
  declarations are reusable by queries; missing/inconsistent declarations have stable RGN diagnostics.
- CLR overload identities remain independent from reference annotations. Recursive port validation
  retains rectangular array rank, nested declaring-type arguments and nullable generic structs.
  API snapshots treat annotation changes as contract changes, not new overloads.
- Matching generic projection constructors honor configured annotations. Exact-inferable LINQ calls
  retain compile-time nullable flow without discarding explicit CLR widening/type-only arguments.
- Library demonstrates nullable collection inputs and nullable scalar results in its handwritten
  application port. Simple and Configured generated outputs remain unchanged; no new adapter is required.

See [Contract nullability](../implemented/repositories/CONTRACT-NULLABILITY.md).

The data-contract/tooling batch adds:

- F14: designer-controlled request/result data inventories in optional version-2 API snapshots.
  Public data members, nullability, required/init-only access, constructor defaults, enum values,
  generic constraints and recursively exposed types are compared with stable RGA diagnostics.
- Version-1 baselines remain readable and unchanged by default. Previously approved data checks
  cannot be silently disabled; the CLI gates DTO-only breaks before any generated-file writes.
- Common module/projection, port, lifetime, provider, loading and paging validation failures now
  have stable RGD prefixes. These are fail-fast configuration checks, not SQL translation proofs.
- Mixed jagged/rectangular array rank/annotation rendering preserves the real CLR type, verified
  by C# compiler/reflection round trips and strict generated application-port compilation.
- Library demonstrates an optional data snapshot candidate; Ledger's separate approved baseline
  and the Simple/Configured APIs remain unchanged. No runtime feature or CLI DSL was added.

See [API compatibility](../implemented/generation/API-COMPATIBILITY.md) and [designer diagnostics](../implemented/generation/DESIGNER-DIAGNOSTICS.md).

The native returning-write batch adds:

- F11: optional `UseReturningWrites<TWriter>()` and named `Update/Delete.ReturnsMany(...)`
  commands backed by SQLite RETURNING or SQL Server OUTPUT. EF-free `MutationResult<T>` exposes
  all directly affected DTOs, without preliminary entity fetches or shared tracker saves.
- Predicates/visibility/version checks remain EF-translated and parameterized. Scalar conversions,
  composite keys, repeated/key-derived setter values, computed fields and binary versions are
  supported; client-only setter expressions are rejected rather than used as raw input values.
- Mapping occurs before owned commit or within a shared savepoint. Actual SQL Server tests cover
  update-locked expected-version/consume races, savepoints and MARS rejection. Cache/work observers
  follow the real commit; existing tracked rows are deliberately not synchronized.
- A sixth Inventory configuration demonstrates this capability on top of simple direct-context
  DI, with an application-owned port and no cache, tenant resolver, leases or unit-of-work setup.
  Existing outputs and the Ledger approved baseline stay unchanged.

See [Returning writes](../implemented/writes/RETURNING-WRITES.md). More providers and provider-specific bulk import remain
explicit strategy extensions; this is not a portable one-round-trip or streaming graph-write promise.

The CQRS-selector batch adds:

- F01: optional `UseCqrsSelectors()` with separate read/write ports and sealed, composition-based
  read selectors. Mutation/staging/save/set-based operations and query escape hooks are absent
  throughout read chains; the combined backing interface stays internal.
- Independent no-tracking defaults on ordinary reads, configured operations and replacement
  roots, with deliberate tracking/identity-resolution overrides and existing hidden/renamed controls.
  Configured projections remain usable when ad-hoc projections/grouping are omitted entirely.
- Entity read/write capabilities, automatically read-only keyless views, shared scoped registration,
  optional legacy cache-decoration and naming/reuse diagnostics. Existing compatibility defaults
  and all prior generated outputs remain unchanged; no owned context or new runtime adapter is required.
- A seventh Workboard example keeps service queries in named designer operations, stages a patch
  and an event through different write ports, and saves their shared context explicitly.

See [CQRS selectors](../implemented/repositories/CQRS-SELECTORS.md). Selector contracts still refer to model classes; EF-free
application-port binding, context leases and contract baselines remain the separate closed mode.

The type-adapter/contract-boundary batch adds:

- F02: explicitly registered, reusable model/application ID and request/DTO expression mappings.
  Markers are expanded at design time throughout operations, including source factories, fragments,
  loading/dependency analysis and write validation. No runtime mapper or generated service dependency
  is introduced; closed contracts remain application-owned and EF-free.
- Selectively mapped inferred primary-key/index inputs, including composite accessors, preserved
  cardinality and deliberate overrides/clearing. Existing EF value converters can supply the reverse
  accessor input expression with exact CLR-type checks and nullable-reference null bypass.
- Independent dictionary/lookup key annotation trees, retaining non-null outer keys and nullable
  nested parts. Accessor inputs also support annotation trees. Exact port binding and API snapshots
  retain key/value shapes; reference annotations cannot hide duplicate CLR accessor overloads.
- F14: stable adapter registration/recursion/property/converter and overload diagnostics. Captured
  ordinary state, dynamic adapter getters and unapproved protected/graph create mappings remain
  rejected, rather than gaining an implicit mapping/reflection escape hatch.
- Two more runnable configurations: TaskDesk's closed typed-ID application ports and a separate
  read-only directory using converter-backed/composite CQRS accessors. Both start with simple
  borrowed contexts, without cache, tenancy, leases or mapper infrastructure.

See [Type adapters](../implemented/repositories/TYPE-ADAPTERS.md) and [Contract nullability](../implemented/repositories/CONTRACT-NULLABILITY.md).

The multi-context/tooling batch adds:

- F14: optional generated persistence namespaces covering selectors/bases, repositories, query/
  projection helpers, legacy result/cache/resolver types and CQRS registration. Shared entity CLR
  types can now have distinct generated repositories in one persistence assembly. Models/DTOs,
  closed application contracts and single-context compatibility placement are unchanged.
- Explicit code-first GenerateMany requests with independent designers/layouts/providers and
  optional snapshot candidates. Deterministic context ordering, generated CLR/port/path collision
  checks and whole-batch ownership/destination preflight happen before any output replacement.
  Context-qualified split infrastructure supports shared directories, including generic contexts.
- Single and batch writers refuse another context's owned C#/manifest/API files and planned or
  existing file-as-parent paths. Checks create no directories; obsolete files are reported per
  context, never deleted. Per-file replacement is atomic, not a multi-file transaction.
- Context-isolated legacy cache keys/tags avoid cross-database row reuse/invalidation for shared
  entities. Context DI, read tracking and saves stay independent; no federation, distributed save,
  implicit tenant partition or shared physical-database invalidation policy is introduced.
- A tenth runnable configuration generates simple primary CQRS and a closed application-owned
  archive read port into one assembly/shared directory over two separate SQLite databases.
  All nine previous applications and their generated/baseline outputs remain unchanged.

See [Multi-context generation](../implemented/generation/MULTI-CONTEXT-GENERATION.md).

The optional membership/query-input batch adds:

- F07: designer `WhereIn`/`WhereNotIn` on entity and projected/grouped/source result stages,
  with strict null/empty defaults and explicit optional-filter/null-element overrides. Per-binding
  deduplication, raw-value bounds and immutable option snapshots stay out of service code.
- Input preparation before context/resolver/keyed-lease acquisition and cache lookup. Snapshots
  are reused across counted pages and streaming; cancellation, enumeration disposal and owned
  context cleanup retain their contracts. Filtering stays server-side with no batching/client fallback.
- Closed EF-free input validation, registered-root/result-stage composition, reusable type-adapter
  expansion, related-navigation dependency inference and optional manifest membership inventory.
  Cached operations require repeatable materialized values; prepared arrays/flags do not escape ports.
- Actual SQL Server large-list, null/negative, joined/grouped HAVING and paging execution through
  parameterized provider `Contains`/OPENJSON. TVP/temp-table deployments remain explicitly registered
  provider roots rather than a silently selected strategy. Core/design have no new provider dependency.
- An eleventh simple borrowed-context SQLite example binds a handwritten read port with named ID,
  optional-category search and grouped-count methods. No cache, tenancy, lease or runtime mapper is
  required; all prior generated files and the approved API baseline are unchanged.

See [Membership queries](../implemented/queries/MEMBERSHIP-QUERIES.md).

The navigation/provider-hardening batch adds:

- F05/F08/F13: optional `RequireNavigationVisibilityReview()` with inherited entity/projection/
  query/command settings and typed policy/reason receipts. Unreviewed inspectable access to
  policy-protected navigation targets fails before output writes. Deterministic manifests record
  mapped edges, reasons, effective requirements and distinct overload identities. Receipts do not
  inject filters, infer authorization, or prove predicate equivalence; the guard is off by default.
- F13: complete inspectable predicate/sort/dictionary/scalar/cursor/source-policy navigation cache
  dependencies, constant-name EF.Property support, and transitive/inherited EF global-filter root
  dependencies. Related writes invalidate root-only cached reads even when the related entity
  appears only in a global filter. Opaque helpers/provider roots still need explicit declarations.
- F03/F08: buffered-stream cancellation checks at yield/completion boundaries in both generation
  paths, including owned closed/registered/result streams. Actual SQL Server split-query buffering
  exposed this bug; canceling now stops returned rows and disposes the owned lease.
- F05/F06/F08: seven actual SQL Server fixtures for composite/left/existence/anti joins, all set
  operators, joined HAVING/pages/dictionaries, filtered nested single/split/paged loading, global
  filter/bypass semantics, projection pruning, early/canceled stream cleanup and translation-error
  cleanup without a client fallback. Fixture DDL/seeds initialize transactionally and own only
  GUID-named tables, never an existing application schema/database.
- A twelfth simple borrowed-context Visibility application exposes summary/detail/history/anti-
  join methods through a handwritten EF-free port. Filtering and review stay in the designer/model;
  services gain no policy bypass, include selection, cache/resolver/lease or unit-of-work dependency.

See [Navigation visibility](../implemented/queries/NAVIGATION-VISIBILITY.md). Existing feature defaults, contract/API
snapshots and approved baselines remain unchanged. Existing stream infrastructure is deliberately
regenerated for the cancellation correction; the optional review guard itself emits no runtime API.

The code-first/contract-validation batch adds:

- F14: an ordinary referenceable `RonSijm.RepoGen.Generation` engine library, shared by the
  standalone Tool. Existing public API namespaces and Tool type forwarding preserve source and
  assembly-qualified lookups. The engine has no CLI, compiler, in-memory or SQL Server dependency;
  the CLI retains its existing project/model loader, invocation and provider behavior.
- F02/F14: bounded closed-contract graph validation covering public bases/interfaces, members,
  indexers, constructors, helper/operators, event callbacks and generic constraints. Relational/
  provider/ADO/query escapes report their exact path with RGD1502. Closed generic arguments remain
  checked even after a definition was seen; expanding `Node<List<T>>` DTOs terminate. Valid strong-ID
  parsing helpers/notification events remain usable, and no application getter/body is executed.
- F14: opt-in `RonSijm.RepoGen.Validation` in-memory C# compilation for bundles and whole batches,
  explicit target references, separate contract compilation, consumer compile fixtures, immutable
  structured diagnostics, cancellation and reviewed warning/language policies. Missing references
  never fall back to the host. Neither ordinary generation/CLI nor generated applications require it.
- Explicit, default-off MSBuild reference export and an optional .NET 8 GenerationInputs fixture.
  `-ValidateCompilation` checks all fifteen examples from a .NET 10 runner using real application
  references and facade-free bootstrap inputs. All model/API/compiler gates precede publication;
  compiler failure exits 5 before creating output directories or writing generated files.

See [Compiler validation](../implemented/generation/COMPILATION-VALIDATION.md). All previously generated files, contracts,
manifests, candidates and the approved baseline are unchanged; there are no new runtime flags.

The command-side query batch adds:

- F01/F02: optional `HasQuery(...).OnWriteRepository()` on entity/current-result builders. Named
  command reads become real instance methods on the selected write port, not the read port,
  including write-only entities and exact overloaded application-owned ports with command members.
  Closed request/results remain EF-free; CQRS selectors can deliberately load tracked entity graphs.
- Independent tracking defaults: command reads inherit ordinary selector/context policy, not the
  no-tracking read-model default. Explicit designer overrides still win. Loading does not save,
  lock, authorize, refresh tracked entries or retry stale client state.
- F03/F04/F05/F06/F08: existing keyed/owned/shared context strategies, transaction participation,
  typed adapters, named load profiles, multi-source/result/group pipelines, scalar/keyed/page/cursor
  terminals and stream cleanup work on the new destination without additional services or CLI flags.
- F13/F14: command reads cannot enable caching. Plain/decorated reads execute against the chosen
  context; existing save invalidation remains. Visibility/global-filter/review rules retain their
  boundaries, with active-query review correctly matched after capability exclusions. Disabled
  query keys/sorting/profile/projection inputs no longer participate in generation validation.
- Additive write-target manifest inventory and correct closed API/data snapshots. Moving a query
  between ports is a reviewable breaking read-contract change, not an automatically accepted baseline.
  Specific diagnostics reject unsupported surfaces, keyless command reads, caching and write-helper
  signature collisions. Resetting the option restores byte-identical defaults on all three surfaces.
- Workboard demonstrates a designer-configured tracked command load and shared staged save;
  TaskDesk demonstrates a DTO-returning query on a handwritten EF-free write port. Only their six
  persistence/query/manifest/API outputs change; all other generated files and the Ledger approved
  baseline remain byte-identical. No extra example project, infrastructure default or dependency is added.

See [Command-side queries](../implemented/queries/COMMAND-SIDE-QUERIES.md).

The infrastructure save-participation batch adds:

- F04: optional typed pre-save participants on ordinary/keyed shared work. Infrastructure can stage
  its own tracked rows on the actual selected context and explicit owned transaction, without
  adding context/callback parameters to generated application contracts. Existing constructors and
  unconfigured saves retain their signatures/behavior; no extra required DI service or CLI flag is added.
- F03/F04: deferred keyed registration, reference-identity de-duplication, sequential preparation,
  cancellation between callbacks, and fail-fast re-entry. Once preparation starts, a preparation/
  native-save failure makes work rollback-only, preventing unsafe partial replay or commit. Owned
  disposal rolls back; borrowed contexts remain caller-owned and must be discarded/reconciled.
- F04/F13: tags are collected after preparation, so participant-owned rows invalidate their dependent
  cached reads only after the real commit. Generated Stage/Save commands retain their saved result
  semantics. Set-based/native/atomic writes and commit do not implicitly invoke this tracked-save hook.
  Explicit execution-strategy attempts can register replayable participants on fresh work/context
  state; failed attempts roll back, and committed observer failures remain non-retryable outcomes.
- Ledger explicitly opts two scopes into a tiny save-receipt adapter and checks tracked patch/receipt
  rollback/commit. Its receipt table is not in the designer allowlist; no generated repository, facade,
  manifest, API candidate or approved baseline needs to change. All simpler examples remain unconfigured.

See [Save participants](../implemented/repositories/SAVE-PARTICIPANTS.md). Application adapters still own messages, publication
decisions, idempotency and delivery; this infrastructure seam is not a durable outbox implementation.

The reusable predicate-composition batch adds:

- F07: immutable designer `And`/`Or`/`Not` fragments, including static/request-bound composition.
  Row/request parameter identities are inlined without invocation nodes or compiled delegates;
  nested binding/capture protection preserves the configured boolean tree and provider translation.
- F06/F07: conditional `UseIf` and result-stage `Use`, with automatic condition-request alignment
  and explicit `WithRequestName` for unconditional reuse. Model-level scalar/DTO/group factories do
  not register fictitious entities. Duplicate model names and invalid aliases have stable RGD IDs.
- F03/F08/F13: existing context-key/validation/lifetime, type-adapter, visibility and inspected cache
  dependency behavior is preserved. Registered roots, grouped HAVING/pages/dictionaries, stream
  cleanup and related-write invalidation execute on SQLite; no runtime query DSL or fallback is added.
- Membership demonstrates optional text matching and grouped count thresholds through its existing
  EF-free application port. The original operation signatures remain; the other eleven examples and
  all approved API baselines retain their generated outputs. Simple generation needs no new option.

See [Reusable query fragments](../implemented/queries/QUERY-FRAGMENTS.md). Optional read clauses cannot stand in for
required policies; command fragments remain unconditional and provider/domain boundaries stay explicit.

The designer SQL-timeout policy batch adds:

- F04: optional entity and named query/command/result-builder SQL-command timeouts, with
  operation → entity → model inheritance. Explicit null retains the selected context's setting;
  `InheritCommandTimeout()` removes only that override. No service timeout arguments or context
  resolver/lease requirement are introduced; unconfigured/reset output remains byte-identical.
- F03/F04/F06/F08/F09/F11/F12/F13: existing lifetime scope restores the exact previous setting on
  completion, failure, cancellation and early stream disposal, before owned context disposal.
  Grouped/counted pages, joined/registered roots, tracked saves/reloads, set-based/native writes
  and cache outcomes execute on SQLite. Staging does not carry its timeout into a later shared save.
- F14: optional deterministic manifest timeout receipts, stable invalid-value/non-closed-surface
  diagnostics, strict all-layout compilation and unchanged EF-free contracts/API snapshots.
  Overrides require a complete closed operation/stream scope; selector escape hatches cannot
  silently activate them. These policies are per-SQL-command limits, not deadlines or retries.
- Reports demonstrates model/entity defaults, grouped-page overrides and explicit context-setting
  retention. Only its persistence and manifest outputs change; other examples remain unconfigured.

See [Command timeouts](../implemented/repositories/COMMAND-TIMEOUTS.md) for inheritance, concurrency and save boundaries.

The optional ranking/limit query batch adds:

- F06/F07: designer-only `OrderByIf`/`OrderByDescendingIf`, `ThenByIf`/`ThenByDescendingIf`,
  `SkipIf`/`TakeIf` and `DistinctIf` over typed entity/projected/grouped/source results. Disabled
  branches omit their SQL stage; disabled counts are not evaluated. Static/request-bound keys,
  constant/request-derived counts and request-alias rebinding require no service query extensions.
- F06/F14: adjacent ordering must be guaranteed on every branch before `ThenBy`; optional sorts
  alone cannot satisfy guarded paging. Grouping/distinct reset the order guarantee. Stable RGD2401
  diagnostics, deterministic conditional-stage manifests and strict nullable/all-layout compilation
  preserve the existing EF-free API when the operation's request type is reused.
- F03/F04/F08/F13: existing visibility, type-adapter, cache identity/dependency and operation lifetime
  behavior remain in force. Grouped/counted/slice/cursor/dictionary queries, joined/registered roots,
  shared write-side reads and stream cleanup execute without a client-evaluation fallback.
- Reports extends its existing application-owned request with inactive-by-default ranking/skip/cap
  options. Its port signatures remain unchanged, and all other examples stay unconfigured. No new
  global feature switch, runtime DSL, CLI argument or required infrastructure dependency is added.

See [Conditional query stages](../implemented/queries/CONDITIONAL-QUERY-STAGES.md) for ordering, paging-count and provider
translation boundaries. Designer authors still own unique tie-breakers and matching cursor predicates.

The explicit dictionary-collision batch adds:

- F06: opt-in `Dictionary(key, duplicateKeys)` / `Dictionary(key, value, duplicateKeys)` policies
  for `KeepFirst` and `KeepLast`. Existing calls and explicit `Throw` retain byte-identical output.
  One provider query selects the configured key/value shape, then applies CLR dictionary equality;
  no per-key reads, count query, SQL window rewrite or client-filter fallback is introduced.
- F06/F14: first/last requires ordering guaranteed on every branch, recognizing ordinary source/
  default sorts and requiring result ordering after replacement roots/grouping/distinct. Stable
  RGD2501/RGD2502 diagnostics, nullable/all-layout compilation and optional manifest receipts
  preserve named EF-free signatures. The designer still owns unique tie-breakers.
- F03/F04/F07/F13: typed key adapters, bound application ports, joined/grouped/capped/registered
  roots, command-side reads, visibility, shared transactions, owned cleanup and timeout restoration
  retain their contracts. Assembly cancellation does not return a partial dictionary. Non-default
  policies have distinct closed/legacy cache identities; default identity remains unchanged.
- Membership adds first/last-per-category operations on its existing handwritten read port and
  proves the hidden row cannot become the selected last value. It still starts with simple borrowed
  contexts and no new infrastructure dependency. Other eleven examples stay unconfigured.

See [Dictionary results](../implemented/queries/DICTIONARY-RESULTS.md) for equality, source-row limits, SQL/provider and
cache boundaries. `Lookup` remains the explicitly duplicate-preserving alternative.

The whitelisted result-sorting batch adds:

- F06/F07: optional result `OrderByField(...)` with typed request selectors and fixed typed key
  expressions over entities, DTOs, anonymous/grouped/scalar/joined/registered results. Public aliases
  are case-insensitive and culture-independent; no runtime field becomes a SQL identifier or
  reflection path. Unknown fields fail before context acquisition or cache lookup.
- F06/F14: explicit unspecified-field rejection, a whitelisted default field/direction, or preserving
  existing fallback order. Branch-aware ThenBy/paging/dictionary guards remain in force, including
  anonymous-row type inference. Captured independent whitelists, deterministic optional manifest
  receipts and stable RGD2601/RGD2602 diagnostics preserve unconfigured output and EF-free APIs.
- F03/F04/F08/F13: selector inputs are snapshotted once for count/items/cache fills; membership
  preparation, request/key adapters, shared transactions, command-side reads, owned stream cleanup,
  timeout scopes and provider errors retain their contracts. All whitelist navigation expressions
  contribute to visibility review and cache dependencies; related writes invalidate cached ordering.
- Reports extends its existing handwritten request with inactive-by-default field/direction choices
  and tests grouped ascending/descending results plus pre-acquisition rejection. Other eleven
  examples remain unconfigured; no new infrastructure service, CLI flag or package is required.

See [Result field sorting](../implemented/queries/RESULT-FIELD-SORTING.md). Designer authors still own unique tie-breakers,
provider-translatable keys and cursor predicates matching the selected field/direction.

The request-bound terminal batch adds:

- F06/F07: optional typed request overloads for dictionary/lookup keys and values, independently
  or together, and Min/Max/Sum/Average mappings across the numeric/nullable families. Terminal-only
  inputs become real named method parameters; paired mappings canonically bind one request without
  capturing nested lambda scopes. Existing row-only configuration remains byte-identical.
- F02/F13/F14: those inputs participate in explicit nullability, exact port signatures, bounded
  EF-free contract validation, API/data inventories, manifests and cache keys. Mapping expressions
  retain adapter expansion, navigation review and inspected related-write dependencies.
- F03/F04: scalar SQL and projected key/value terminals preserve source policies, command-side
  reads, shared transactions, owned cleanup, input guards, cancellation and timeout restoration.
  Dictionary assembly remains final buffering, not a client predicate/aggregate fallback.
- Reports adds grouped label/value mappings and a requested scalar total on its handwritten port.
  The other eleven examples remain unconfigured, with no new service, CLI or package dependency.

See [Request-bound terminals](../implemented/queries/REQUEST-TERMINALS.md) for parameter naming, overloads, SQL and
cache boundaries. Provider numeric support, domain arithmetic and stable request values remain
explicit consumer responsibilities.

The request-aware cursor batch adds:

- F06/F07: optional typed-request seek and cursor-mapping overloads on entity and result builders,
  independently or together. Paired mappings share the seek's request parameter with capture-safe
  rebinding. Terminal-only inputs participate in annotations, ports and API/data inventories.
- F03/F13: optional cursor-only or request-aware continuation guards, evaluated only for supplied
  cursors before context acquisition/cache lookup. First pages skip guard bodies; invalid or
  replaced cursor configurations produce stable diagnostics. New request cursors reject null page
  requests and cancellation before acquisition, and retain ordinary lifetime/timeout/cache behavior.
- F02/F14: EF-free input/cursor validation, exact handwritten ports, optional manifest receipts,
  adapter expansion and deterministic output across all supported layouts/surfaces. Unconfigured
  cursor declarations and simple repositories retain their existing output.
- Reports adds a guarded region/direction-aware report on its existing application port. Services
  call one named method and stop based on HasMore; no runtime query or cursor-mapping DSL is exposed.

See [Request-aware cursor paging](../implemented/queries/REQUEST-CURSORS.md). Ordering/tie-breaker correctness, provider
translation, stable input values, token signing and authorization remain explicit designer/application
responsibilities; guards do not synthesize or authenticate continuation tokens.

| Feature | Status and remaining work |
| --- | --- |
| F01: CQRS/selectable surfaces | Closed-operation mode and optional CQRS-selector read/write ports delivered. Named command-side queries are configurable on write ports, including write-only entities, with independent tracking and read-port isolation. Sealed read selectors retain read chains but cannot inherit/cast to writes or expose raw query hooks. Entity capabilities/keyless reads, shared scoped registration and independent no-tracking read defaults are executed. Compatibility selectors intentionally remain combined; alternate read/write stores require explicit application registration. |
| F02: EF-free contracts/ports | Contract output, result abstractions, named instance methods, exact overloaded port binding, explicit outer/nested annotations and independent dictionary keys/values delivered. Bounded public graph validation reports ORM/query escapes through bases, helpers/operators, event callbacks and constraints. Optional independent contract/consumer compilation checks actual C# boundaries. Reusable ID/request/DTO adapters and selected EF-converter-backed accessors are available. Configure operation names/types to match ports. Arbitrary business/graph reconciliation, runtime mapper services and automatic type-wide conventions are deliberately not inferred. |
| F03: async lifetime/selection | Owned/borrowed operation/work leases, streaming disposal, buffered yield/completion cancellation, fixed typed keys and designer request-to-key acquisition delivered. Shared keyed work rejects database switches. Selection/authorization remain application-owned. |
| F04: unit of work/commit | Shared lifetime, transactions, tags, committed-failure semantics, explicit replayable execution-strategy runner, designer model/entity/operation SQL timeouts with scoped restoration, closed-cache commit integration and optional infrastructure pre-save participants delivered. Participants stage tracked rows on the shared owned transaction, with rollback-only failure and fresh-attempt safeguards. Durable outbox/delivery remains application-owned, not a built-in guarantee. |
| F05: multi-source queries | Typed expression factories, inner/left/composite joins, SelectMany, correlated existence/anti-joins, set operations, source inventories and dependency inference delivered with SQLite and representative SQL Server execution. Optional navigation policy review and source-policy/filter dependency hardening are implemented. Arbitrary provider-specific shapes still require execution tests; a separate join DSL is not required, and no cross-database federation is implied. |
| F06: post-group/projection pipeline | HAVING/filtering/sorting, regrouping, distinct, scalars/dictionaries/lookups, slices, result cursors, default-sort control, explicit stable counted pages and optional paging-order diagnostics delivered. Optional primary/secondary sorts, limits and distinct preserve inactive SQL shapes and require ordering on every branch for guarded paging/ThenBy. Whitelisted result field sorting uses fixed typed aliases and explicit missing-field/default/fallback policies. Dictionary collisions reject by default, with explicit ordered first/last policies and duplicate-preserving lookups. Unique tie-breakers remain designer-authored, not automatically inferred. |
| F07: typed request binding | Shared requests across query/command clauses, explicit group aggregates, request-aware dictionary/lookup and scalar terminals, reusable AND/OR/NOT fragments, conditional entity/result reuse, optional ranking/limits/distinct, explicit request aliases and pre-acquisition guards delivered. Model-level DTO/scalar fragments do not register EF entities. Optional designer membership adds explicit null/empty/null-element policies, deduplication and raw-input limits on roots/results/groups/registered sources, with snapshot reuse and safe enabled-cache validation. SQL Server large-list OPENJSON/null/HAVING/paging behavior is executed. Existing Contains predicates remain available; TVP/temp-table strategies and domain parsing remain explicit infrastructure/application adapters. |
| F08: navigation chains/profiles | Typed chains, named profiles on queries/projections/defaults, request-bound filters/limits, model validation, deliberate single/split policies, optional multiple-collection/navigation-review guards and projection auto-loading control delivered. SQLite and representative SQL Server graph/paging/pruning/cancellation shapes execute. No service-side runtime flags or automatic cost optimization are introduced; other provider-specific shapes still need tests. |
| F09: typed create/patch | Scalar create/partial patch, bounded CreateMany, explicit Save/Stage, Optional fields, saved DTOs/generated values, approved/protected assignments and failure restoration delivered. Optional ReturnsSaved adds fresh complete-key DTO/navigation queries after single or bounded batch saves, with input-ordered batch values. Optional ProtectWrite adds scalar policy and local AllowWrite permissions across tracked/set-based/native/atomic and collection mappings, without runtime authorization claims. Bounded leaf-child reconciliation and [explicit two-FK link reconciliation](../implemented/writes/LINK-RECONCILIATION.md) add owner-scoped matching, approved add/update/missing-item policies, limits, versions and restoration. Links reference existing targets without mutating them; [explicit shared-reference columns](../implemented/writes/SHARED-REFERENCE-COLUMNS.md) preserve complete matching and owner consistency. [Named additional references](../implemented/writes/ADDITIONAL-LINK-REFERENCES.md) add complete create-only FK maps with immutable matches. [Explicit optional additional references](../implemented/writes/OPTIONAL-ADDITIONAL-REFERENCES.md) add all-or-none nullable creation and immutable presence/values. Arbitrary aggregate/business-rule mapping is not inferred. |
| F10: expected-version outcomes | Original concurrency binding, tracked Conflict/NotFound, conditional set-based NotMatched and shared-save conflict attribution delivered with SQLite tests and actual SQL Server database-generated rowversion races, saved values and null patches. Optional bounded root batches now bind versions per item, reject partial missing-key application, restore all target changes on failure, and require relational transaction/savepoint safeguards when saving. No stale-client automatic retry. |
| F11: atomic upsert/returning | Optional race-tested SQLite/SQL Server atomic and native UPDATE/DELETE returning writers delivered, alongside tracked consume. Native multi-row DTOs, converted assignments, version races and rollback/savepoints are executed. Other providers and bulk import remain opt-in strategies, not portable promises. |
| F12: temporal/registered SQL roots | Registered composable parameterized roots, provider diagnostics and optional SQL Server All/AsOf/ranges/period DTOs delivered and tested on actual SQL Server. Non-composable stored procedures remain handwritten port adapters. |
| F13: cache scope/visibility | Scope-aware closed adapter, memory implementation, commit invalidation, transaction/pending bypass, complete inspectable clause/global-filter dependency expansion, required root visibility/trusted bypass and optional navigation-review receipts delivered. Review is not filtering/authorization. Opaque dependencies, distributed coordination/durable delivery remain explicit backend/application adapters. |
| F14: scale/tooling | Modules/manifests, referenceable engine library/CLI adapter, split output, safe writes/checks, independent runner/build integration, closed source-contract baselines and optional data-shape compatibility delivered. Optional compiler validation adds explicit target references, separate contract/consumer checks and whole-batch compilation without writes/execution; opt-in MSBuild exports actual resolved inputs. Namespace isolation/multi-context batches support shared CLR entities and split directories with CLR/port/path/ownership preflight. Stable IDs cover common boundaries; additional argument/expression diagnostics remain incremental. Bound ports remain handwritten. Full binary/wire/provider compatibility is not claimed. |
| F15: generated test doubles | Optional scripts, typed recording/overrides, coverage receipts, keyed stores/copies/baselines and portable single/multi-source criteria/query/projection/navigation reads are delivered, including [many-to-many stores](../implemented/testing/INMEMORY-MANY-TO-MANY.md), [guarded nullable links](../implemented/testing/INMEMORY-OPTIONAL-LINKS.md), [explicit null-preserving reports](../implemented/testing/INMEMORY-NULL-TARGETS.md), [designer-declared scalar-call translations](../implemented/testing/INMEMORY-TRANSLATIONS.md) and [context scalar/key bindings](../implemented/testing/INMEMORY-MODEL-FILTER-CONTEXTS.md). Public scalar maps, tracked scalar/bounded batches, [CreateMany](../implemented/writes/CREATE-MANY-COMMANDS.md), explicit values/versions and shared Stage/Save/checkpoint rollback are available. Immediate scalar writes/insert/upsert, [model-filtered atomic branches](../implemented/testing/INMEMORY-ATOMIC-FILTERS.md), [child](../implemented/testing/INMEMORY-COLLECTIONS.md)/[composite](../implemented/writes/COMPOSITE-COLLECTION-KEYS.md)/[link reconciliation](../implemented/writes/LINK-RECONCILIATION.md), [saved DTOs](../implemented/writes/SAVED-COMMAND-RESULTS.md) and [batch results](../implemented/writes/SAVED-BATCH-RESULTS.md) remain explicit choices, as do [per-command scalar translations](../implemented/testing/INMEMORY-COMMAND-TRANSLATIONS.md), [tracked scalar equality policies](../implemented/testing/INMEMORY-VALUE-EQUALITY.md), [shared-reference columns](../implemented/writes/SHARED-REFERENCE-COLUMNS.md), [named additional references](../implemented/writes/ADDITIONAL-LINK-REFERENCES.md) and [explicit optional roles](../implemented/writes/OPTIONAL-ADDITIONAL-REFERENCES.md). Contract suites/provider fixtures exercise generated implementations, with forty-five focused testing examples. The [capability map](INMEMORY-REPOSITORY-FEATURE-MAP.md) describes the generic testing requirements. Broader graphs/value/provider policies remain extensions; arbitrary entity behavior and database equivalence are not inferred. |

## Earlier verification

Before the subsequent slices below: Release solution build completed with zero warnings/errors. The full solution
test run with the opt-in local SQL Server fixtures enabled passed all **1,056 tests** (52 EF integration,
992 tool/generation/provider, 10 attribute-generator, and 2 attribute tests), with no skips/failures.
Without `REPOGEN_SQLSERVER_TEST_CONNECTION`, relational SQL Server fixture tests are deliberately
skipped; normal SQLite-only runs are not represented as SQL Server verification. Atomic/rowversion
database tests use that same opt-in environment variable and also own only GUID-named tables.

All twelve configuration examples regenerated, built with zero warnings/errors, and executed against
SQLite. The independent runner also built with generated facade inputs excluded for bootstrap;
a subsequent normal Release solution build restored and verified the regular contract inputs.
`-Check -Run -ValidateCompilation` passed with real .NET 8 reference exports, separate EF-free
contract compilation and facade-free bootstrap inputs. Earlier MultiContext/Visibility opt-in
pre-compile freshness checks remain covered. Missing-output batch checks
create no directories, and the existing CLI freshness/no-write tests still pass. A subsequent
generation wrote zero changed files. The request-bound terminal batch deliberately changed only Reports'
persistence and manifest outputs; all forty-eight other inventory files remain byte-identical,
including every generated contract, API candidate and approved baseline. Its request type and two
new signatures are declared in the existing handwritten application port. Before opting the example
in, regenerating with the new overload implementation changed none of the fifty inventory files.
The earlier result-field sorting batch deliberately changed only Reports'
persistence and manifest outputs; all forty-eight other inventory files remain byte-identical,
including every generated contract, API candidate and approved baseline. Its existing handwritten
request gained optional field/direction properties; application-port method signatures are unchanged.
The earlier dictionary-policy batch deliberately changed only Membership's
persistence and manifest outputs; all forty-eight other inventory files remain byte-identical,
including every generated contract, API candidate and approved baseline. Its two new operation
signatures are declared in the existing handwritten application port. The earlier optional-stage batch deliberately changed only Reports'
persistence and manifest outputs; all forty-eight other inventory files remain byte-identical,
including every generated contract, API candidate and approved baseline. The designer-timeout batch deliberately changed only Reports'
persistence and manifest outputs; all forty-eight other inventory files remain byte-identical,
including every generated contract, API candidate and approved baseline. The predicate-composition
batch deliberately changed only Membership's persistence and manifest outputs; all forty-eight other inventory files remain
byte-identical, including every generated contract, API candidate and approved baseline. The prior
save-participation batch changed none of the fifty inventory files. The command-side query batch deliberately changed six
Workboard/TaskDesk persistence/query/manifest/API outputs; all other forty-four inventory files
remain byte-identical, including the separately checked-in Ledger approved API baseline. The previous
navigation batch added three Visibility outputs and deliberately updated twelve persistence/infrastructure
outputs for the streaming cancellation correction. All thirty-five other previously checked-in
outputs were unchanged, including prior manifests, contracts, API candidates and the approved
baseline. Regeneration does not automatically accept or rewrite that baseline.

Forty-three request-terminal tests cover every mixed/request-bound key/value and scalar shape,
all supported layouts/surfaces, every numeric/nullable overload, strict compilation, terminal-only
input discovery, nullable input/value annotations, EF-free graph checks, exact bound ports and
unchanged API signatures when an existing input is reused. Paired binding protects nested scopes;
excluded mappings do not change outputs and captured state/reserved names remain invalid. SQLite
execution verifies one pruned pair/aggregate query, grouped mappings, empty/null/cardinality behavior,
dictionary collision/null-key policies, adapter expansion, membership/field-sort preparation, owned
failure/cancellation cleanup, input guards, shared transactions and command-side reads. Actual cache
hits separate inputs, clone dictionary/lookup values and invalidate related navigation mappings;
navigation reviews remain required when configured. One opt-in SQL Server fixture executes typed
request mappings, grouped dictionaries/lookups, parameterized strings and decimal/nullable numeric
aggregates on GUID-owned tables, preserving visibility, pruning and scoped timeout restoration.

Fifty-three earlier result-field sorting tests cover null/blank/duplicate/default/callback/stale diagnostics,
independent immutable whitelist capture, deterministic alias ordering, capture-safe request binding,
all supported surfaces/layouts, strict nullable/anonymous compilation, exact bound EF-free ports,
unchanged API snapshots and unconfigured output. SQLite execution verifies fixed alias/direction
branches, inactive ordering/column omission, grouped/joined/distinct/count/slice/dictionary/cursor
pipelines, input rejection and cancellation before owned acquisition, once-only count/items input
preparation, membership coexistence, expanded type adapters, registered/shared/command-side reads,
timeouts and owned stream disposal/provider failure. Culture/quoted-alias checks prove strings never
become SQL identifiers. Actual cached reads distinguish resolved choices for equal serialized requests,
clone their results and invalidate ordering after related writes; whitelist navigations require review.
One opt-in SQL Server test executes field-sorted/pruned and aggregate-ranked pages plus default/
preserve/rejection policies on GUID-owned tables, retaining visibility and scoped timeout behavior.

Forty-five earlier dictionary-policy tests cover invalid/null/stale inputs, terminal replacement, ordering
on every branch, every supported surface/layout, explicit/default byte-identical Throw output,
immutable model capture, unchanged EF-free contracts/API snapshots and optional manifest receipts.
SQLite commands verify one key/value query, column pruning, explicit row selection, required visibility,
row-valued/scalar/nullable/empty/unique/case-sensitive keyed results, joined/grouped/distinct/capped
pipelines, typed key adapters/bound ports, registered shared work and command-side reads. Owned
null-key failure and cancellation at the first/last assembly insertion restore timeout/disposal contracts. Actual closed
cache hits are clone-safe and policy-separated; legacy shape keys differ without changing default
keys. One opt-in SQL Server test executes ordering/pruning, grouping, row caps and default collision
failure against GUID-owned tables, preserving filters and scoped timeout restoration.

Fifty-eight earlier optional-stage tests cover null/negative/stale inputs, every static/request-bound sort
overload, independently enabled branches, skipped count evaluation, zero/negative request counts,
capture-safe request rebinding, expanded type adapters, strict nullable/all-layout compilation,
unchanged EF-free contracts/API snapshots and deterministic manifests. SQLite commands verify
inactive-sort omission, optional distinct, grouped/joined/count/slice/cursor/dictionary pipelines,
registered shared-transaction roots, command-side reads, required visibility, owned stream cleanup,
scoped timeout restoration, cache request identity and conservative navigation dependencies. An
additional opt-in SQL Server test executes optional ranking, offsets/caps and aggregate-ranked
grouped pages without client fallback; its fixture owns only GUID-named tables.

Forty-four earlier timeout-policy tests cover operation/entity/model inheritance, explicit context-setting
retention, local reset, immutable model capture, inactive/excluded configurations, invalid/stale
diagnostics, exact unconfigured/reset output, stable overload manifests and unchanged independently
EF-free contracts/API snapshots. All three closed layouts compile without warnings. Actual SQLite
commands verify entity/model/operation policies, grouped/joined/count/slice pages, registered roots
inside shared transactions, input guards, owned/keyed acquisition, restoration before owned disposal,
nullable original settings, stream cancellation/early disposal, translation/dictionary/SQL cancellation
failures, tracked save/reload and later staged saves, set-based/native writes, native rollback, cache
hits/fill failures and committed invalidation failures. The timeout-era Reports example inspected nine SQL
timeouts; its current expansion also checks field-sorted reads. No provider-independent wall-clock deadline or automatic retry is claimed.

Forty-six earlier fragment tests cover immutable static/request composition, boolean precedence,
request-name/identity alignment, nested binding/capture protection, null/stale/duplicate diagnostics,
all supported surface/layout compilations and independently EF-free consumer contracts. Relational
generated execution verifies inactive clauses, DTO/scalar/From/group roots, HAVING/pages/dictionaries,
stream cancellation/disposal, unconditional set-based write reuse, required visibility, keyed work,
registered shared-transaction roots, expanded type adapters and actual related-write cache invalidation.
Unsupported active filters fail through EF without client fallback; equivalent reuse and unused
model fragments retain byte-identical generated output.

The earlier code-first/contract-validation batch added no generated outputs and changed none of
those fifty previously checked-in files. Independent generation still writes zero changed files. The
ordinary CLI also validates a designer/model without writes after the engine-library split.
Local package inventories contain the ordinary Generation/Validation library assets; Validation
depends on Generation, not the dotnet-tool package, and the Tool bundles the shared engine without
Roslyn/Validation. Old assembly-qualified engine lookups resolve through the Tool's forwarders.

Thirty-two save-participation tests cover shared-context transaction requirements, reference identity/
ordering/registration freeze, borrowed and owned disposal, keyed acquisition, native/preparation/tag
failures, cancellation, rollback-only work and fail-fast re-entry. Generated command/cache execution
verifies typed Stage/Save results, tracked and set-based shared rollback, participant-type dependent
cache invalidation after commit, fresh-context transient replay and non-replay of committed observer
failures. Ledger's opted-in receipts leave all fifty generated inventory files/baseline byte-identical.

The current save-participation batch adds no generated files and changes none of that fifty-file
inventory. All twelve examples pass compiler/freshness gates, build and execute, including the
receipt scopes; ordinary independent generation still writes zero changed files.

Fifty-four new command-side query tests cover closed/CQRS layouts, actual compiled consumer
boundaries, exact overloaded application ports, DTO/input adapters, capability omissions and
byte-identical defaults. SQLite execution verifies independent and explicit tracking, tracked
aggregate loading, cached-read isolation/invalidation, fixed keyed work, pre-acquisition guards,
owned stream disposal/cancellation, shared transactions/rollback, visibility and navigation review,
and grouped/page/slice/dictionary/cursor result pipelines. API/data snapshots report operation moves
and include only active data shapes. Configuration diagnostics reject unsupported placement,
keyless write targets, command-state caching and generated write-helper signature collisions.

Sixty-eight new tests cover the contract/compiler batch. They exercise all surfaces/layouts,
public graph/operator/event/constraint leaks, inherited relational types, System-namespaced
application DTOs, bounded expanding graphs, parsing/event compatibility, no getter execution,
unused capability omissions and source/assembly forwarding. Actual compiler checks prove EF-free
contract inputs and denied read-port writes, target reference failures/ambiguity, immutable and
deterministic reports, source locations, warning/language policy, cancellation, no generated-code
execution, and multi-context no-write validation. A failing example-runner compiler gate is also
executed directly: exit 5 and no generated output directory created.

Fifty-four tests cover the previous navigation/provider-hardening batch: forty-three navigation-review
tests, three transitive query-filter dependency tests, seven actual SQL Server query/loading tests,
and one attribute-generator buffered-stream cancellation regression. They verify strict generated
compilation on all three surfaces, scope/override/overload receipts, unchanged default behavior,
active-operation validation, complete inspectable cache dependencies and actual related-write
invalidation. SQL Server execution checks composite/left/anti joins, set operators, grouped pages,
dictionary pruning, request-filtered nested single/split loading, global filters and explicit
historical bypass, owned stream cancellation/disposal, and translation-failure cleanup. The review
guard neither filters navigation rows nor exposes runtime policy controls to consumers.

Fifty-two membership tests cover strict generated compilation on all three surfaces/layouts,
isolated namespaces, immutable options, null reference/value elements and negation, explicit
empty/omitted input semantics without bypassing visibility, input bounds before dropped duplicates/
nulls, exact bound ports, EF-facing input rejection, type adapters, source joins/group HAVING,
registered roots, dictionary column pruning, snapshot reuse across counted pages, early streaming
cleanup, cancellation/one-shot enumeration, closed/legacy caching and overloaded manifest/cache
identity even for requests with identical serialized data. Three opt-in SQL Server tests
execute a 6,000-value JSON parameter, paging, nullable/negative string inputs including SQL-looking
text, and joined/grouped membership; fixtures own only uniquely named tables.

Forty-three multi-context tests strictly compile all three surfaces/output layouts and compare
their top-level CLR inventories against the actual compiled types. SQLite execution covers shared
entities in independent contexts, mixed CQRS/closed DI, read tracking/save isolation, configured
projections, ordinary/result/source/group page/cursor pipelines and two decorated repositories
sharing a cache backend without row reuse/cross-database invalidation. Tests cover preserved EF-free
port/snapshot shapes and user literals, presets/clearing, namespace/reuse diagnostics, generic
context filenames, generated type/port/path/parent collisions, whole-batch no-write preflight,
cross-context C#/manifest/API ownership and deterministic shared-directory obsolete-file checks.

Forty-five adapter/key-annotation tests cover actual parameterized primary/composite SQLite
lookups, strong EF keys and structs without equality operators, null-bypassing converters,
closed overloaded ports and saved generated values, expanded source/group/cursor/write clauses,
loading/dependency inventory, annotations and cardinality after clearing, all three output layouts,
protected/graph write rejection, registration/getter/recursion diagnostics and CLR overload identity.
Dictionary/lookup keys and values compile strictly and execute on closed, compatibility and CQRS
surfaces; independent key annotations bind reflected ports and participate in API compatibility.

Thirty-three CQRS-selector tests cover separate consumer compilation, denied read mutation/casts,
ordinary and named tracking policies, source roots/identity resolution, hidden projection entry
points, filtered/ordered pages, dictionaries, grouping, aggregates, streaming/cancellation, scoped
shared-save isolation, visibility policies, optional decorated reads/fluent-write invalidation,
keyless/capability exclusions, parameter/member/type naming edges and both output layouts.

New relational tests verify approved typed writes, original-version races, partial/null patches,
saved-result failures (including legacy invalidation), owner guards, scalar generated values,
shared saves/staging/rollback, scope partitioning, transactional cache bypass/observers, in-flight
cache invalidation, sealed direct-context cached writes and result cloning. Native SQLite duplicate/upsert races and tracked consume
races each have one winner. Actual SQL Server temporal tests exercise periods and UTC roots/ranges.
Contract tests execute overloaded nullable read/write ports and independent bypass/cache shapes.
Paging tests inspect actual seek SQL, shared read transactions and restored timeouts. Generation
tests compile both layouts and verify deterministic/safe writes and obsolete-file reporting.

The foundation has compile tests for EF-free contracts and read capability isolation, and tests that compile **and execute** generated implementation code against SQLite. They cover projected reads, offset and cursor pages, immediate commands, shared registrations, application-owned ports, missing/incompatible ports, keyless read contracts, exclusions, and unsupported configurations.

Lifetime/transaction tests cover owned versus borrowed disposal, canceled acquisition, factory use without synchronous creation, fixed-key database isolation, early stream disposal, shared unit-of-work context identity, explicit commit versus rollback, tag deduplication, invalid tags before save, and observer failures after a committed write. CLI tests cover validation without output writes and duplicate-path rejection before writing.

Query-pipeline tests execute generated code and inspect SQLite SQL: HAVING, grouped counted pages, distinct counts, key/value column pruning, single-query slices, anonymous/composite grouping, request conditions/projections/sorts/limits, unordered loads, and rejection of unsupported client filters. They verify nullable/missing scalar results, empty aggregates, legacy restricted projections/cache branches, new EF-free contract shapes, stream cancellation/disposal, and generation-time diagnostics.

Profile tests verify omitted optional helpers, retained direct-context CRUD, selective re-enabling, unchanged compatibility output, preset ordering, independent policy preservation, no-designer CLI use, case-insensitive values, and rejection of unsupported profile names.

Multi-source tests compile and execute generated repositories on SQLite and inspect SQL for joins, EXISTS/anti-joins, all four set operations, grouping/HAVING, counted pages, and pruned dictionaries. They cover filters/bypass, tracking/tags, one resolver acquisition, early/canceled stream disposal, actual dependent-tag invalidation after a saved related-repository write, EF-free contracts, unconfigured defaults, and generation/runtime failure cleanup.

Tracking tests compile the generated surface and execute entity-materializing SQLite queries, not just DTO/scalar queries that would avoid tracking anyway. They cover hidden/renamed controls, selective omission, identity resolution, fluent type/callback preservation, persisted writes from an untracked selector, independent/default/explicit read policies on ordinary and From roots, direct/leased pages and streams, pending shared unit-of-work writes, and invalid names/selector reuse diagnostics.

Loading tests compile and execute generated reference/collection/skip-navigation graphs against SQLite, including optional references and owned metadata, request-filtered nested collections and limits, single/split SQL shape, counted pages, streams, input-only loading parameters, bound ports, legacy projection/default loading, equivalent/conflicting filters, and deeper cache dependencies. They check scalar DTO column pruning and compare an unsupported APPLY-requiring shape with raw EF, preserving the provider error and disposing the owned lease rather than silently filtering on the client.

Provider claims are bounded: SQLite atomic/version/consume races and actual SQL Server temporal,
atomic/composite-key, rowversion, tracked consume and native UPDATE/DELETE OUTPUT fixtures are
executed. Native SQLite RETURNING tests also cover null semantics, nullable converter directions,
repeated/key-derived assignments, full server setter translation and savepoint/cache behavior.
No universal provider conformance is claimed. Fixtures own only uniquely named tables.
Tooling tests cover all three layouts, deterministic API roundtrips, inherited/default port metadata,
caller/implementer changes, handwritten snapshot protection and CLI baseline overlap/no-write gates.

Twenty-four contract-type tests cover nullable collections/elements/results, exact port binding,
value-type overload identity, generic DTO constructors, named projection inheritance, ordinary/From/group
roots, legacy and closed output, pages/slices/cursors/streams, dictionary/lookup values, saved null values,
cached-read invalidation, recursive reflected shapes, immutable configuration snapshots and RGN/RGA
diagnostics. Generated-source warning checks and the actual .NET 8 Library build both pass.

Fifty-one additional tooling tests cover actual reflected DTO/request changes, data-shape capture
at the exposed operation boundary, read/write annotations, required/init rules, fields/inheritance,
constructor defaults, enums, generic CLR/nullable constraints, cyclic data graphs, schema versions,
immutable metadata and DTO-only CLI no-write gates. Stable designer IDs are tested across fourteen
configuration failures. Mixed-rank array signatures round-trip through a real C# compilation and
reflection; explicit annotation trees bind and compile the real application interface.

## Optional bounded collection commands

- F09/F10: closed tracked Patch commands may explicitly `ReconcileCollection` on an ordinary keyed
  one-to-many leaf navigation. The item type is fixed in a dedicated child-setter builder; services
  receive only a named method and an EF-free request/result.
- No additions/updates are inferred. Missing items default to Keep; Reject and tracked Delete are
  deliberate designer policies. New entities use scalar initializers, stable supplied matching keys
  and generated ownership from the selected parent's complete FK. Key/FK/store-generated protections
  are checked at generation time; skip/owned/inherited/shared/nested graph shapes remain unsupported.
- Inputs are bounded snapshots checked before context acquisition. Null/duplicate/oversized inputs
  fail early, and existing visible rows are loaded with a bounded relationship query. Optional
  request conditions distinguish omission, explicit null and empty replacement without runtime flags.
- EF global filters and configured child visibility policies constrain mutation, including when
  hidden rows already exist through tracker fixup. Read/result projections still need deliberate
  visibility configuration; this is not authorization inference. Implicit lazy loading is suppressed
  only for collection commands and the context's exact previous setting is restored on every exit.
- Parent and child expected versions require actual unconditional setters for conditional SQL
  writes. Save/Stage, owned-lease guards, saved DTOs/generated child values, cancellation, explicit
  transaction rollback and shared-save semantics reuse the existing infrastructure.
- Failed saves, child conflicts and later-collection errors restore affected values/membership/states
  and detach new children while preserving unrelated pending edits. Target collections with pending
  edits are rejected. No stale-client retry, automatic orphan unlinking or bulk-import engine is added.
- F13/F14: child and dependent query-owner invalidation tags include namespace-isolated child
  repositories; optional manifest entries explain keys, limits, assignments and deletion policies.
- Added a focused [Collections RepositoryDesigner example](../../samples/ConfigurationExamples/Collections/README.md)
  with two contrasting generated methods, exact declaration/output links and executable SQLite checks.
  The collection extension is absent from the other twelve examples' regenerated outputs.

See [collection reconciliation](../implemented/writes/COLLECTION-RECONCILIATION.md) for the API, policies and boundaries.

## Collection batch verification (2026-10-07)

The Release solution build completed with zero warnings/errors. The four test projects passed
**1,127 tests**: 1,063 tooling/generation/provider tests, 52 EF integration tests, 10 attribute-generator
tests and 2 attribute tests. Twenty-four SQL Server tests were skipped because
`REPOGEN_SQLSERVER_TEST_CONNECTION` is not configured; this run is not SQL Server runtime verification.

The suite includes 41 new collection-reconciliation tests covering generated execution, save/stage,
owner isolation, bounds and omitted/null/empty inputs, parent/child version conflicts, failure
restoration, shared transactions, visibility/global filters, owned leases, lazy-loading suppression,
cache invalidation and invalid designer shapes. Read-only collections and EF-ignored setters are
checked explicitly rather than allowed to undermine membership restoration or concurrency guards.

All thirteen configuration examples passed `-Check -Run -ValidateCompilation`, including real .NET 8
target references and separate EF-free contract compilation. They built without warnings/errors and
executed their SQLite assertions. The new Collections example adds three generated files; the other
twelve examples' persistence, contracts, manifests and API/baseline outputs remain unchanged.
Local documentation links and `git diff --check` also passed.

## Optional designer write-protection batch

- F09/F14: `ProtectWrite` lets a closed RepositoryDesigner protect ordinary mapped audit/ownership
  scalars without naming conventions, runtime permission flags or mandatory infrastructure. No
  declaration leaves the existing generated surface and output unchanged; declarations can be removed.
- Explicit `AllowWrite` belongs to the assigning command or child mapping. Permissions do not leak
  between operations or from parent commands to children, including children without repositories.
  Existing immutable-key, generated-value and relationship ownership restrictions still apply.
- Validation covers tracked create/patch Save and Stage, set-based updates, native returning updates,
  atomic create/update mappings and bounded child additions/updates. Unsupported selectors and public
  selector surfaces fail during generation rather than implying protection around a bypass.
- Optional manifests expose protected properties and declared permission receipts. Policy-only changes
  do not add public methods or change API snapshots; review the designer/manifest for behavioral changes.
  This is a mapping guard, not authorization or inspection of arbitrary constructors/interceptors.
- The focused [WriteSafety RepositoryDesigner example](../../samples/ConfigurationExamples/WriteSafety/README.md)
  maps its three declarations directly to generated Create, Rename and CorrectAttribution methods,
  using a simple borrowed context, EF-free contracts, ownership predicates and expected versions.
- Collection diagnostics now use their own `RGD2801`/`RGD2802` family, distinct from conditional-ordering
  diagnostics. Write-policy diagnostics use `RGD2901` through `RGD2903`.

See [write protection](../implemented/writes/WRITE-PROTECTION.md) for defaults, assignment paths and deliberate boundaries.

## Write-protection batch verification (2026-10-07)

The Release solution build completed with zero warnings/errors. All four test projects passed
**1,156 tests**: 1,092 tooling/generation/provider tests, 52 EF integration tests, 10 attribute-generator
tests and 2 attribute tests. Twenty-four SQL Server tests were skipped because
`REPOGEN_SQLSERVER_TEST_CONNECTION` is not configured; this run is not SQL Server runtime verification.

The suite includes 29 new write-protection cases covering supported command paths, strict generated
compilation, SQLite execution, local permission isolation, child policies without repositories,
immutable metadata restrictions, manifest receipts, invalid configurations and exact output stability
when the policy is absent or removed. All 70 focused write-protection/collection checks also passed.

All fourteen configuration examples passed `-Check -Run -ValidateCompilation`, including real .NET 8
target references and separate EF-free contract compilation. They built without warnings/errors and
executed their SQLite assertions. WriteSafety adds three generated outputs; the other thirteen
examples' persistence, contracts, manifests and API/baseline outputs remain unchanged.
Local documentation links and `git diff --check` also passed.

## Optional root-row batch commands

- F09/F10: opt-in closed `PatchMany` / `DeleteMany` map bounded request items to explicitly declared
  scalar primary/alternate/unique-index keys. Item builders fix the item type, approved scalar setters,
  optional-field conditions and original expected versions; services call a single named operation.
- Input snapshots, null/duplicate/limit checks and cancellation precede context acquisition. Empty
  batches return NoChanges without a context/query/save. One filtered parameterized key query loads
  targets; global filters and designer scope remain authoritative even for already-tracked hidden rows.
- Save and Stage remain explicit. Saving calls the shared SaveChanges once and returns row DTOs in
  input order; the count is still shared-context changes, not item count or one SQL roundtrip. Staging
  returns no saved values. Missing/hidden/other-owner keys fail the whole batch rather than being skipped.
- Target pending edits are rejected. Pre-save failures and target version conflicts restore all batch
  scalars/originals/flags, preserving unrelated edits; conflicts elsewhere are not misattributed.
  Mapped changes also persist with automatic change detection disabled. Result failure after a saved
  write retains the committed-result exception semantics rather than inviting a blind retry.
- Saving requires relational automatic transaction protection and supported/enabled savepoints for
  an existing context transaction. Ambient transactions, cascading deletion, navigation mappings/results,
  composite/array keys, owned/shared/inherited entities and mixed command shapes are explicit boundaries.
- F02/F13/F14: EF-free list-result contracts, exact bound ports, nullable rows, registered type-adapter
  expansion, isolated namespaces, split layouts, deterministic manifest receipts, scoped caching and
  existing work/timeout/commit behavior are retained. `RGD3001` / `RGD3002` cover batch validation.
- Added the focused [BatchWrites RepositoryDesigner example](../../samples/ConfigurationExamples/BatchWrites/README.md)
  with exact designer/output links, partial/null edits, per-item conflicts, staging and a disjoint shared
  save. No cache, tenancy, lease/native-writer provider or unit-of-work registration is required.
  The other fourteen examples' outputs remain unchanged; absent declarations add no API/infrastructure.

See [batch commands](../implemented/writes/BATCH-COMMANDS.md) for defaults, contracts and persistence boundaries.

## Batch-command verification (2026-10-07)

The Release solution build completed with zero warnings/errors. All four test projects passed
**1,218 tests**: 1,154 tooling/generation/provider tests, 52 EF integration tests, 10 attribute-generator
tests and 2 attribute tests. Twenty-four SQL Server tests were skipped because
`REPOGEN_SQLSERVER_TEST_CONNECTION` is not configured; this run is not SQL Server runtime verification.

All 62 new focused batch-command cases also passed. They cover strict generated compilation, EF-free
bound ports, nullable results, all output layouts, adapter expansion, bounded one-shot inputs, cancellation
before context acquisition, per-item versions, ownership/global filters, shared-save conflict attribution,
tracker restoration, save/mapper failures, disabled automatic detection, cache invalidation, context
ownership, explicit rollback and transaction/savepoint guards. New runtime coverage uses SQLite.

All fifteen configuration examples compiled without warnings/errors and passed their executable
assertions. Freshness and strict compilation checks against actual .NET 8 target references and separate
EF-free contracts passed for every generated bundle; the approved Ledger API baseline remains compatible.
BatchWrites adds three generated outputs; the other fourteen examples' outputs remain unchanged.
All 167 local links checked in the batch guide, progress/feature plan and example documentation resolved,
and `git diff --check` passed.

## Delivered scripted test-double slice (2026-10-07)

The [in-memory capability map](INMEMORY-REPOSITORY-FEATURE-MAP.md) describes F15.1/F15.5 and the first F15.6 examples:

- Designer opt-in `UseScriptedTestDoubles(namespace)`, nullable entity overrides and separate test output in all layouts; default-off generation preserves ordinary output, dependencies and API snapshots.
- Exact closed read/write/combined and bound-port implementations, including overloads, nullable/application values and inherited handwritten default helpers. EF-facing selectors are not claimed as supported.
- Optional EF-free .NET 8 Testing runtime: typed matchers, queued responses/failures, fallback/async handlers, strict unarranged calls, pre/post cancellation checks and cold streams with disposal outcomes.
- Counts and bounded opt-in argument history; explicit copy adapters detach mutable requests at capture/inspection. Result/exception objects are not retained; in-flight completions do not resurrect cleared history.
- Explicit scoped aliases replacing selected ports only when test registration is called. Ownership/freshness/path/type checks include testing files; an independent compiler stage verifies their EF-free boundary.
- RequiresOverride receipts for every script and `executesDesignerPipeline: false`; no default empty/success behavior, domain rules or SQL simulation.
- Focused [ScriptedPorts](../../samples/ConfigurationExamples/ScriptedPorts/README.md) and [ScriptedStreams](../../samples/ConfigurationExamples/ScriptedStreams/README.md) RepositoryDesigner examples, exact artifacts and EF-free consumers. The existing fifteen examples' outputs remain unchanged.

See [scripted test doubles](../implemented/testing/SCRIPTED-TEST-DOUBLES.md) for configuration, priority and limits.
The following shared-store slice delivers F15.2. The later portable-read slice delivers part of F15.3
and shared fake/SQLite behavioral cases for F15.6. Compound-source/navigation reads, stateful writes,
Stage/Save/rollback simulation remain pending. The later contract-fixture slice delivers the reusable
relational fixture harness. Existing
real-provider tests are still needed for SQL, concurrency and transactions; domain/external adapters
stay consumer-owned.

## Scripted-test-double verification (2026-10-07)

The Release solution build completed with zero warnings/errors. All five test projects passed
**1,287 tests**: 1,190 tooling/generation/provider tests, 52 EF integration tests, 10 attribute-generator
tests, 33 EF-free testing-runtime tests and 2 attribute tests. Twenty-four SQL Server tests were skipped
because `REPOGEN_SQLSERVER_TEST_CONNECTION` is not configured; this run is not SQL Server runtime verification.

The 69 new focused cases cover exact generated/bound contracts, overloaded methods, nullable values,
all output layouts, diagnostic/type/output collisions, explicit scoped registration, independent
EF-free compilation, CLI generation/freshness, matcher/queue/fallback priority, cancellation, bounded
copied history, concurrent queue consumption, reset races and cold-stream iteration/disposal failures.
History reports the actual escaping stream exception, including cleanup failures that supersede an
earlier iteration failure or cancellation.

All seventeen configuration examples passed `-Check -Run -ValidateCompilation`, including actual
.NET 8 references and separate EF-free contract/test-double compilation. ScriptedPorts and
ScriptedStreams add eight generated artifacts; both consumer dependency graphs exclude EF, models,
designers and the generation engine. The original fifteen examples' generated outputs remain unchanged,
and the approved Ledger API baseline remains compatible. Local documentation links and
`git diff --check` were checked successfully.

## Delivered shared in-memory fixture slice (2026-10-07)

F15.2 adds optional test-only storage, alongside the already generated scripts:

- Model `UseInMemoryTestStores(bool)` is off by default. Entity `HasInMemoryStore<TKey>(key, copy)`
  stores an explicit entity shape; `HasInMemoryStore<TData, TKey>(key, copy)` selects a public data proxy,
  including for inaccessible database types. `WithoutInMemoryStore()` removes one declaration.
- Shared context fixture/data classes in every output layout, injected into all selected doubles.
  Default scoped registration isolates tests; explicit pre-registered fixture lifetimes are preserved.
  Generated contracts/production output/API snapshots and the previous seventeen examples stay unchanged.
- EF-free .NET 8 storage runtime with immutable typed keys, explicit detached copy adapters,
  one-shot atomic SeedRange/ReplaceWith, Throw/Ignore/Replace duplicate policies, Find/Snapshot,
  immediate fixture Remove/Clear and consumer-owned deterministic seed keys/factories.
- Cross-store baseline capture/restore prepares every copy before changing any store; failure leaves
  all states intact. Restores keep store/data identities stable and copy baselines independently.
  Script reset/history reset never silently resets data.
- Optional typed context partitions use the configured context key type. Equal keys share state;
  other keys and their baselines remain isolated. Partition reset is local, not an all-key transaction.
- Explicit storage receipts record row/key shapes, sources, lifetime and baseline scope while retaining
  RequiresOverride operations and `executesDesignerPipeline: false`. Generation diagnostics reject
  missing prerequisites/stores, invalid/inaccessible adapters and reserved Fixture member collisions.
- Focused [MemoryFixtures](../../samples/ConfigurationExamples/MemoryFixtures/README.md) and
  [MemoryPartitions](../../samples/ConfigurationExamples/MemoryPartitions/README.md) RepositoryDesigner
  examples connect each declaration to exact output and an EF-free consumer. The latter uses a
  public storage proxy, typed partitions and deliberately shared singleton data across scoped scripts.

See [shared in-memory fixtures](../implemented/testing/INMEMORY-FIXTURES.md) for configuration and exact copy/reset boundaries.
Storage alone does not execute operations. The later portable-read slice adds explicit automatic
read lowering, and the contract-fixture slice adds the real-provider harness. Fake generated-value/version
simulation, stateful writes and Stage/Save/rollback remain planned. Immediate arrangement APIs do not establish persistence
correctness.

## Latest shared-fixture verification (2026-10-07)

The Release solution build completed with zero warnings/errors. All five test projects passed
**1,334 tests**: 1,211 tooling/generation/provider tests, 52 EF integration tests, 10 attribute-generator
tests, 59 EF-free testing-runtime tests and 2 attribute tests. Twenty-four SQL Server tests were skipped
because `REPOGEN_SQLSERVER_TEST_CONNECTION` is not configured; this run is not SQL Server runtime verification.

The 47 new focused cases cover generated stores and public storage proxies, all output layouts,
strict compiled contracts, typed context keys, scoped/singleton sharing, option/definition opt-outs,
public type/expression prerequisites, ordinary/generic default-helper collisions, storage receipts,
deep-copy isolation, invalid/null/same-root/key-changing copiers, key invariants, duplicate policies,
one-shot inputs, failed enumeration/copy preparation, concurrent arrangement/partition selection,
re-entry guards and atomic multi-store baseline failures. Seeded but unarranged operations still fail.

All nineteen configuration examples passed `-Check -Run -ValidateCompilation`, including actual
.NET 8 references, separate EF-free contract/test compilation and the approved Ledger API baseline.
MemoryFixtures and MemoryPartitions add eight generated artifacts; both executable dependency graphs
exclude EF, Models, Designers and the generation engine. The previous seventeen examples' generated
outputs remain unchanged. All 410 local links checked in the affected guides, roadmap and example
documentation resolved; `git diff --check` passed.

## Delivered portable fixture-read slice (2026-10-07)

F15.3 and F15.5 add optional generated read execution without a second service-side query DSL:

- `UseInMemoryTestQueries()` is off by default and requires shared fixture stores. Existing script-only
  fixtures, production sources, contracts, API snapshots and cache-query fingerprints remain unchanged.
- Entity-shaped stores execute the supported single-store designer model: criteria/fragments, request
  guards, membership preparation, visibility/bypasses, sorting/field whitelists, named projections,
  anonymous result pipelines, groups/regrouping, scalar/keyed terminals, counted pages, uncounted slices,
  entity/result cursors and cold streams. Typed context-key expressions select the fixture partition.
- One explicitly copied snapshot supplies each invocation or stream enumeration. LINQ runs outside
  the fixture lock; cancellation is checked at execution/enumeration boundaries. Counted page Items
  and TotalCount share the same transformed snapshot. SQL/collation/null/isolation semantics are not
  fabricated, and mutable request objects are not silently deep-cloned.
- Every operation has Generated/RequiresOverride receipts and deterministic reasons. EF model filters,
  navigation/loading/inheritance, compound/provider roots, custom helpers/ambient state and public
  proxy-expression adaptation are explicit boundaries, even for an empty store.
- Optional `RequirePortableTestQueries()` rejects unacknowledged non-portable reads. Per-query
  `RequireTestOverride(reason)` acknowledges a typed scripted implementation without changing its
  production definition. Writes and command-side reads remain scripted rather than pretending to save.
- Matching queues/rules and explicit handlers override the automatic backend. Script reset preserves
  the backend, recording policy and fixture rows; baseline restore remains a separate data operation.
- Focused [MemoryCriteria](../../samples/ConfigurationExamples/MemoryCriteria/README.md) and
  [MemoryCoverage](../../samples/ConfigurationExamples/MemoryCoverage/README.md) RepositoryDesigner examples
  link each declaration to exact outputs and EF-free executable consumers. F15.6 also gains shared
  behavioral cases comparing the same named operations with real generated SQLite repositories.

See [portable in-memory reads](../implemented/testing/INMEMORY-QUERIES.md) for the capability matrix and meaningful limits.

## Latest portable-read verification (2026-10-07)

The Release solution build completed with zero warnings/errors. All five test projects passed
**1,374 tests**: 1,245 tooling/generation/provider tests, 52 EF integration tests, 10 attribute-generator
tests, 65 EF-free testing-runtime tests and 2 attribute tests. Twenty-four SQL Server tests were skipped
because `REPOGEN_SQLSERVER_TEST_CONNECTION` is not configured; this run is not SQL Server runtime verification.

The 40 new focused cases cover separate EF-free compilation in every output layout, unchanged
production/contracts/API/cache fingerprints when testing is toggled, exact bound parameter names,
nullable scalar results, the supported query/result/terminal shapes, request guards and membership
limits, safe root/result field sorting, partitions, cold streams, cancellation, overrides/reset,
strict acknowledgements and unsupported-shape receipts. Shared cases also compare named operations
with real generated SQLite repositories, including grouping, dictionaries, pages and cursors.

All twenty-one configuration examples passed `-Check -Run -ValidateCompilation`, including actual
.NET 8 references, separate EF-free contract/testing compilation and the approved Ledger API baseline.
MemoryCriteria and MemoryCoverage add eight generated artifacts. All six testing consumer dependency
graphs exclude EF, Models, Designers and the generation engine. Existing example outputs remain
unchanged. All 772 local links checked in the affected guides, roadmap and example documentation
resolved; `git diff --check` passed.

## Delivered contract-suite and real-provider fixture slice (2026-10-07)

F15.6 now includes reusable consumer testing infrastructure, independent of generation switches:

- The optional EF-free Testing runtime adds immutable named `RepositoryContractSuite<TRepository>`
  assertions with explicitly owned per-case sessions. The same application-port cases run against
  different backends. Names/failure phases, fail-fast execution, cancellation and combined
  assertion/cleanup failures are retained without taking a test-framework dependency.
- Separate `RonSijm.RepoGen.Testing.EntityFrameworkCore` owns a DI provider and real-provider fixture
  sessions. It validates scoped context registration/provider shape and opens fresh scopes for
  actual generated/application repositories. Ordinary Testing/contracts remain EF-free.
- Provider and generated registrations, schema initialization, seed and reset are explicit callbacks.
  No creation/deletion/reset is inferred. Reset refuses open sessions/concurrent reset; callbacks
  remain responsible for dedicated test-database ownership and partial-failure recovery.
- Setup/cleanup failures are both preserved. Disposal closes every tracked session and provider,
  shares its completion across callers and rejects reentrant access before user cleanup runs.
- [RelationalContractFixture](../../samples/ConfigurationExamples/RelationalContractFixture/README.md)
  maps one designer to real/fake outputs and shares five read cases across generated fixtures and
  SQLite. [RelationalWrites](../../samples/ConfigurationExamples/RelationalWrites/README.md) verifies
  actual generated IDs, expected versions, two staged repositories/shared save and rollback from
  fresh scopes, using the existing neutral unit-of-work contracts.
- No new designer/CLI flag, production registration, default test dependency or implicit fake-write
  behavior is added. The previous twenty-one example outputs remain unchanged.

See [contract suites and relational fixtures](../implemented/testing/REPOSITORY-CONTRACT-FIXTURES.md) for APIs and boundaries.

## Latest contract-fixture verification (2026-10-07)

The Release solution build completed with zero warnings/errors. All five test projects passed
**1,401 tests**: 1,245 tooling/generation/provider tests, 66 EF integration tests, 10 attribute-generator
tests, 78 EF-free testing-runtime tests and 2 attribute tests. Twenty-four SQL Server tests were skipped
because `REPOGEN_SQLSERVER_TEST_CONNECTION` is not configured; this run is not SQL Server runtime verification.

The 27 new focused cases cover immutable named contract suites, fresh per-case sessions, failure phases,
fail-fast behavior, cancellation including final cleanup, combined assertion/setup and cleanup failures,
idempotent/concurrent disposal, explicit provider/setup/reset callbacks, scoped context validation,
non-relational provider rejection, shared context/save boundaries, reset lifecycle guards and disposal
of every owned session/provider even when cleanup fails.

All twenty-three configuration examples passed `-Check -Run -ValidateCompilation`, including actual
.NET 8 references, separate EF-free contract/testing compilation and the approved Ledger API baseline.
RelationalContractFixture and RelationalWrites add seven generated artifacts; the previous twenty-one
examples' outputs remain unchanged. The six existing EF-free testing consumers retain dependency graphs
without EF, Models, Designers or the generation engine. The two new consumers explicitly opt into the
real-provider fixture package and SQLite. All 819 local links checked in the affected guides, roadmap
and example documentation resolved; `git diff --check` passed.

## Delivered multi-source fixture-read slice (2026-10-07)

F15.3 now lowers explicitly designed typed From/Set roots across shared fixture stores:

- Existing `UseInMemoryTestQueries()` enables the portable source subset. There is no extra option,
  runtime query DSL, inferred relationship or production dependency. Generated fixtures contain
  typed Enumerable calls, not EF/Queryable calls; designer expressions are never executed at generation.
- Inner/composite/left joins, correlated Any/anti-joins, SelectMany cross joins, scalar set operations,
  source grouping and conditional roots compose with the existing request/result pipelines and terminals.
- One `InMemoryDataSnapshot` captures only distinct participating stores under one partition's context
  lock. Self-joins reuse one captured table. Queries execute outside the lock; cold streams acquire a
  fresh snapshot on first move and retain it for their enumeration. This is not database isolation.
- Every actual root's visibility is applied before composition. Owner-only policies/default sorts do
  not leak into replacement roots. All sources must have selected entity-shaped stores; referenced EF
  filters, AutoInclude, inheritance, navigation, proxy shapes and unsupported operators retain explicit
  override reasons and strict checking. A source-only query needs no unused owner store.
- Optional source receipts list distinct roots, lowered operators and coordinated snapshot boundaries.
  Existing scripts, reset/recording behavior, production/API/cache shapes and default output are unchanged.
- [MemoryJoins](../../samples/ConfigurationExamples/MemoryJoins/README.md) connects one designer to seven
  application-port operations and exact real/fake files. Its consumer checks composite/left joins,
  correlated anti-joins, unions, grouped pages, shared DI identity, cold streams and baseline restore
  without any EF/model/designer/generation runtime dependency.

See [portable multi-source reads](../implemented/testing/INMEMORY-QUERIES.md#multi-source-queries-use-the-same-designer-definitions)
and [shared snapshot ownership](../implemented/testing/INMEMORY-FIXTURES.md). Navigation fixup and fake writes remain separate work.

## Latest multi-source verification (2026-10-08)

The final Release solution build completed with zero warnings/errors. All five test projects passed
**1,441 tests**: 1,275 tooling/generation/provider tests, 66 EF integration tests, 10 attribute-generator
tests, 88 EF-free testing-runtime tests and 2 attribute tests. Twenty-four SQL Server tests were skipped
because `REPOGEN_SQLSERVER_TEST_CONNECTION` is not configured; this is not SQL Server runtime verification.

The 41 new focused cases cover typed source lowering in every layout, unchanged production/API/cache
shapes, distinct root/operator receipts, visibility before joins and explicit bypass, independent source
selection, strict unsupported-shape checking, keyed partitions, inner/composite/left/self/cross joins,
correlated predicates, set operations, grouping/requests/dictionaries, conditional roots, nullable scalars,
slices/result cursors, cold streams, cancellation and overrides/reset. Shared cases compare actual
generated SQLite operations. Snapshot-runtime cases cover detached copies, names/types, duplicate/empty
selection, unused failing stores, copy/re-entry failures, unchanged baselines and concurrent capture.
The obsolete expectation that every From source requires an override was removed.

All twenty-four examples passed `-Check -Run -ValidateCompilation`, with actual .NET 8 inputs,
independent EF-free contracts/testing compilation and the approved Ledger API baseline. MemoryJoins
adds four generated artifacts; the previous twenty-three bundles remain unchanged. All seven fake-only
consumer dependency graphs exclude EF, Models, Designers and the generation engine. All 940 checked
local documentation links resolved; the 32 multi-source source/example files have no trailing spaces
or tabs, and `git diff --check` passed.

## Delivered public-proxy read slice (2026-10-08)

F15.3 now adapts deliberately declared public fixture rows to selected persistence scalars:

- `HasInMemoryStore<TData, TKey>(key, copy).MapInMemoryEntity<TData>(initializer)` declares direct
  scalar assignments. Generated test code constructs a plain internal read row, never the original
  entity or its constructors/setters. `WithoutInMemoryEntityMapping()` removes only the map;
  replacement/removal of a store never retains a stale map. All testing settings remain opt-in.
- Mapped reads reuse predicates, active visibility/default sorting, dynamic field whitelists,
  named projections, groups, aggregates/dictionaries, pages/cursors/streams and typed context keys.
  Explicit multi-source roots capture public proxy stores together and map/filter them before
  joins, null-safe left joins, anti-joins and scalar set operations.
- Coverage checks every referenced scalar across query/policy/result/cursor expressions. Missing
  fields require an override, not fabricated defaults—even for empty stores. Active bypass/sort
  opt-outs do not require unused members. Strict coverage/typed overrides retain their existing rules.
- RGD3203 covers mismatched store shapes, invalid initializers/scalar assignments, inaccessible
  types/constants/casts, nested entity construction and reserved read-row declarations. Registered
  type-adapter markers expand inside assignment values. Nullable fields and explicit detached
  mutable scalar copies retain their declared semantics.
- Whole-entity behavior/identity, model casts/type tests, navigation/model policies, providers and
  writes retain conservative override boundaries. The initializer is not a reverse DTO mapping or
  a new service-side DSL. Production query/contracts/API/cache shapes are unchanged.
- Store `readMapping` receipts list the read-row type and assigned members, explicitly reporting
  no persistence-entity construction and no write adaptation. Internal helper identities participate
  in the existing generated-type collision checks and all output layouts.
- [MemoryProxies](../../samples/ConfigurationExamples/MemoryProxies/README.md) adds a focused designer,
  actual internal EF model, distinct public seed/result types, bound application port, four checked-in
  artifacts and a model/EF-free executable. The example index now contains 25 applications, including
  ten testing examples (eight with independently EF-free consumers).

See [explicit public mappings](../implemented/testing/INMEMORY-PROXIES.md) for the declaration contract, boundaries and
designer-to-output example. Navigation/fixup and F15.4 stateful fake writes/Stage/Save/version/rollback
remain planned; ordinary public scalar mapping is delivered, not arbitrary entity-expression adaptation.

## Latest public-proxy verification (2026-10-08)

The complete Release solution build passed with zero warnings/errors. The full no-build/no-restore
test run passed **1,490 tests**: Tool 1,324, EntityFrameworkCore 66, Generator 10, Testing 88 and
Attributes 2. Twenty-four real-SQL-Server tests were skipped because no test connection is configured;
this is not SQL Server runtime verification.

The 49 additional focused cases cover mapping controls/adapter expansion, no model-assembly reference,
all output layouts, unchanged production/API/cache query shapes, scalar member coverage in every
query/policy/cursor/terminal position, strict acknowledgements, private types/casts, nested entity
construction, reserved/helper names, entity identity/behavior, nullable values, detached mutable scalars,
joins/left joins/anti-joins/unions, groups/dictionaries/aggregates/pages/cursors, cold streams, cancellation,
context partitions, baselines, scripts/reset and unsupported writes. Shared named-operation cases run
against the actual generated SQLite implementation, with internal models accessible only to that
separate persistence test assembly.

All twenty-five applications passed `-Check -Run -ValidateCompilation` against actual .NET 8 inputs,
independent contract/testing references and the approved Ledger baseline. MemoryProxies adds four
generated artifacts; regenerating the previous twenty-four bundles changed none of their files.
All eight fake-only dependency graphs exclude EF, Models, Designers, Design and Generation.
Documentation links and source whitespace checks passed, as did `git diff --check`.

## Delivered declared-navigation read slice (2026-10-08)

F15.3 now loads explicitly selected public entity-shaped relationships for detached fixture reads:

- Per-entity `HasInMemoryNavigation(member)` resolves direction, cardinality, requiredness and
  composite/alternate foreign/principal keys from the final EF model. Nested relationships belong
  to their own entity builders. Repeated declarations are idempotent; `WithoutInMemoryNavigation`
  removes one edge. RGD3304 validates direct actual navigation selectors. No new global option,
  default output, dependency or service-side DSL is added.
- Referenced relationships across predicates/policies/sorting/projections/groups/result stages/cursors,
  From roots and resolved unfiltered Include/ThenInclude/load profiles share one participating-store
  snapshot. Only accessed/loaded edges are rebuilt. Explicit self/inverse paths reuse detached rows;
  no recursive/implicit inverse or cross-call tracking is inferred.
- References use indexed unique target keys; collections receive new lists. Null key components
  never join; equality is ordinary ordinal CLR/structural equality. Duplicate unique-side keys and
  dangling required references fail explicitly rather than fabricating relationships. Loading
  changes neither fixture rows nor baselines, and remains outside the data context lock.
- Actual root visibility still applies before query composition, not implicitly inside navigation
  collections. Shared designer predicates or existing reviewed alternatives own related visibility.
  Active EF filters, AutoInclude, inheritance, proxy navigation, filtered loading, shadow/custom keys,
  read-only/custom collections and skip/many-to-many navigation retain strict override boundaries.
- Per-operation navigationSnapshot receipts list stores, edges and direction-specific key expressions,
  explicitly reporting no implicit related visibility or write adaptation. Production/contracts/API/cache
  query shapes and existing script/recording/reset behavior are unchanged.
- [MemoryNavigation](../../samples/ConfigurationExamples/MemoryNavigation/README.md) adds a focused
  designer, EF context, public fixture/DTO/port inputs, four checked-in artifacts and an EF/model-free
  consumer. The example index now has 26 applications, including eleven testing examples and nine
  independently EF-free consumers. Shared tooling cases compare generated nested reads with actual
  generated SQLite operations rather than hand-written reference queries.

See [navigation-aware fixture reads](../implemented/testing/INMEMORY-NAVIGATION.md) for configuration, semantics and limits.
Public-proxy relationship adaptation and F15.4 stateful fake writes remain separate roadmap work.

## Latest declared-navigation verification (2026-10-08)

The complete Release solution build passed with zero warnings/errors. The full no-build/no-restore
test run passed **1,535 tests**: Tool 1,354, EntityFrameworkCore 66, Generator 10, Testing 103 and
Attributes 2. Twenty-four real-SQL-Server tests were skipped because no test connection is configured;
this is not SQL Server runtime verification.

The 45 added cases cover declaration/removal and disabled settings, all output layouts and unchanged
production/contracts/API/cache shapes, composite/alternate/nullable keys, required and optional
references, nested collections, explicit self/inverse identity, coordinated/keyed snapshots,
detached DTOs, deliberately incorrect seeded graphs, root-versus-related visibility, pages/cold
streams, cancellation, scripts/reset, duplicate/missing keys and the conservative provider/property
boundaries. The configured-comparer regression reads full design-time configuration metadata:
optimized runtime models lose annotations, while ordinary nullable comparers must remain supported.
Shared cases compare ten named invocations with actual generated SQLite repositories, not hand-written
reference queries; their read-only operations leave the EF change tracker empty.

All twenty-six applications passed `-Check -Run -ValidateCompilation` against actual .NET 8 inputs,
independent contract/testing references and the approved Ledger baseline. MemoryNavigation adds four
generated artifacts; regenerating the previous twenty-five bundles changed none of their files.
All nine fake-only consumer dependency graphs exclude EF, Models, Designers, Design and Generation.
Repository-local documentation links and navigation source whitespace checks passed, as did
`git diff --check`.

## Delivered tracked-scalar fixture write slice (2026-10-08)

F15.4 now simulates explicitly designed tracked commands for ordinary public entity-shaped stores:

- Optional `UseInMemoryTestCommands()` is independent of automatic fixture queries and stays off
  by default. `RequirePortableTestCommands()` validates coverage; command-level
  `RequireTestOverride(reason)` acknowledges unsupported shapes. RGD3401/RGD3402 and manifest
  receipts expose prerequisites and per-operation boundaries. Testing-only command models do not
  change production/contracts/API/cache fingerprints.
- Generated handlers reuse command predicates, guards, visibility, conditional approved setters,
  explicit tokens, context keys, Stage/Save and result mappings. Scripts retain precedence.
  Public scalar state uses ordinary CLR/structural equality; full design-time metadata rejects custom
  comparer/conversion and generated-value semantics rather than approximating them.
- `InMemoryWriteDataContext` implements `IRepositoryUnitOfWork`. The consumer deliberately selects
  its fixture context/partition instead of generation replacing a global unit of work. Stage owns
  detached pending rows while reads see saved rows. Expected tokens use original state. Approved
  columns merge without replacing stale unassigned state; mutable setter inputs are copied before Stage.
- Shared Save prepares every touched store before committing any. Conflicts, adapter failures and
  pre-commit cancellation cannot partially save another repository. Outcomes distinguish
  Staged/NoChanges/NotFound/Conflict/Applied. Saved result mapping failures are reported after
  persistence; direct shared saves retain conflicting pending state for explicit resolution.
- Checkpoint transactions roll back saved and pending changes across the partition. They require
  clean staging at Begin, reject nesting/SQL isolation claims, and do not implicitly Save at Commit.
  Baselines and script/history reset remain independent; resets cannot discard pending writes silently.
- [MemorySharedWrites](../../samples/ConfigurationExamples/MemorySharedWrites/README.md) adds a focused
  designer, caller-key/token EF model, public contracts, four generated artifacts and an EF/model-free
  executable. Shared tooling cases compare the named commands, shared saves and rollback with actual
  generated SQLite implementations, not hand-written reference queries. The example index now has
  27 applications, twelve testing examples and ten independently EF-free consumers.

See [stateful fixture writes](../implemented/testing/INMEMORY-WRITES.md) for configuration, save/transaction semantics and
coverage boundaries. Generated-value adapters, public-proxy writes, batches/graphs and native writes
remain explicit extensions; SQL constraints, cache observers and provider races are not simulated.

## Tracked-write verification (2026-10-08)

The Release solution build passed with zero warnings/errors. The full no-build/no-restore suite
passed **1,578 tests**: Tool 1,379, EntityFrameworkCore 66, Generator 10, Testing 121 and Attributes 2.
Twenty-four real-SQL-Server tests were skipped because no connection is configured; this is not
SQL Server runtime verification.

The 43 added cases cover options/prerequisites/strict acknowledgements, all output layouts and
unchanged production/contracts/API/cache shapes, helper-name safety, generated-key/native/reload
boundaries, guards/visibility, conditional assignments, original versions, scalar/binary equality,
mutable input ownership, same-row accumulation, missing/duplicate/conflicting rows, approved-column
merging, atomic multi-store preparation, cancellation/adapter failures, saved result failures,
transaction rollback/commit/lifetime/reset limits, scripts and typed context partitions. Shared cases
compare thirteen named calls plus shared saves and stored state with actual generated SQLite
repositories. The runtime dependency guard explicitly permits only EF-free Abstractions, checking
that library's own references as well as rejecting ORM/model/generator dependencies.

All twenty-seven examples passed `-Check -Run -ValidateCompilation` using actual .NET 8 inputs,
independent contract/testing references and the approved Ledger baseline. MemorySharedWrites adds
four generated artifacts; regenerating the previous twenty-six bundles changed none of their files.
All ten fake-only dependency graphs exclude EF, Models, Designers, Design and Generation. The
927 repository-local documentation links across 71 guides, write-slice source whitespace and
`git diff --check` passed.

## Delivered bounded fixture batch slice (2026-10-08)

F15.4's independently optional write backend now also executes suitable designed PatchMany and
leaf DeleteMany operations:

- Production and fixtures share provider-neutral bounded input preparation in Abstractions, with
  single enumeration, ordered keys/items, finite limits, duplicate/null rejection and cancellation.
  Generated production source remains unchanged; the EF helper delegates to the common preparer.
- Generated handlers reuse the existing root predicates, visibility/bypasses, context-key selector,
  guards, conditional approved assignments, per-item original tokens, Stage/Save and result mappings.
  A declared unique scalar matching column can differ from the fixture's complete primary key.
- Every target is validated/prepared before pending state is published. Missing/hidden/stale targets,
  setter/copy failures and preparation cancellation cannot partially stage earlier items. Target rows
  with existing modifications must be saved/discarded first; unrelated pending rows are preserved.
- Shared Save prepares all stores atomically. Results retain input order, including unassigned items
  in a partially assigned batch. Delete results use pre-deletion values. Saved mapping failure and
  unrelated shared-save conflicts retain their explicit existing exception boundaries.
- Patch/Delete can use **known existing** generated-on-insert values without generating anything.
  Create still rejects generated values; computed-on-update values, database rowversions, graph/native
  behavior and proxy reverse mapping remain explicit overrides/real-provider cases.
- The focused [MemoryBatches example](../../samples/ConfigurationExamples/MemoryBatches/README.md)
  declares a generated-on-insert primary key, separate unique Code matching, protected owner, ordinary
  version tokens and optional nullable titles. Four checked-in artifacts link directly to its designer.
  Its executable has no EF/model/designer dependency and checks atomic outcomes, ordered DTOs,
  Stage/Save, checkpoint rollback and independent baselines. The index now contains 28 applications,
  thirteen testing examples and eleven independently EF-free consumers.

See [fixture batch semantics](../implemented/testing/INMEMORY-BATCHES.md). Tests compare actual generated SQLite and fixture
repositories, not handwritten reference implementations. SQL constraints/collation/isolation, generated
values and provider races are not claimed. Ordinary production/contracts/API/cache shapes and all
unselected options remain unchanged.

## Bounded-batch verification (2026-10-08)

The Release solution build passed with zero warnings/errors. The full no-build/no-restore suite
passed **1,612 tests**: Tool 1,396, EntityFrameworkCore 66, Generator 10, Testing 138 and Attributes 2.
Twenty-four real-SQL-Server tests were skipped because no connection is configured; this is not
SQL Server runtime verification.

The 34 added batch cases cover shared ordered/one-pass bounded input preparation, null/duplicate/limit
validation, cancellation, Stage/shared counts, missing/hidden/stale last targets, setter/copy failures,
dirty versus no-op target state, unrelated conflicts, alternate scalar matching/composite store keys,
empty/unsupplied/unchanged/mixed outcomes, saved result failures, delete values, checkpoint rollback,
scripts/reset, typed context partitions and all output layouts. Actual generated SQLite/fixture named
contract cases run with both supplied and generated-on-insert primary-key metadata. Production,
contracts, API snapshots and cache model shapes remain unchanged when automatic batches are disabled.

All twenty-eight examples passed `-Check -Run -ValidateCompilation` with actual .NET 8 references,
independent contract/testing compilation and the approved Ledger baseline. MemoryBatches adds four
generated artifacts; the only existing output change is MemorySharedWrites' manifest capability receipt.
The other twenty-six bundles regenerate unchanged. All eleven fake-only dependency graphs exclude
EF, Models, Designers, Design and Generation. All 1,112 repository-local documentation links across
73 guides resolved. Source whitespace and `git diff --check` also passed.

## Delivered explicit fixture generated-value slice (2026-10-08)

F15.4's independently optional write backend now supports explicit scalar generated-value policies:

- `HasInMemoryGeneratedValue(property, factory)` declares a pure expression over the candidate and
  detached same-store rows. Entity-shaped stores and automatic fixture commands are prerequisites;
  nothing is enabled by default. Removing an adapter or replacing/removing its store is explicit.
- Actual mapped generation metadata selects insert/update timing. Insert/default generation respects
  the supported CLR sentinel and before-save behavior; computed values always use their factory.
  Adapters run in declaration order. Captures, ambient clocks/random, navigations, unsuitable setters,
  ordinary non-generated properties and unsupported metadata fail with stable `RGD3501` diagnostics.
- Create factories run before staging against saved rows plus all reserved pending keys, so a seed-aware
  allocator does not issue duplicate IDs to several staged creates. Values are detached and reservations
  publish only after successful preparation. Pure row-derived allocation replays after checkpoint or
  baseline restore without an external counter reset. This is not EF temporary-key or SQL identity behavior.
- Update factories run only during atomic all-store Save preparation, after approved assignments merge
  into saved candidates. No-op updates and deletes do not regenerate values. Original-token conflicts,
  cancellation or adapter failure cannot partially commit other pending stores. Generated scalar saved
  values appear in single-command DTOs and input-ordered batch DTOs; existing saved-mapping failure
  semantics remain explicit. Known generated-token deletes do not require unused value adapters.
- Checked conversion/negation/arithmetic expressions retain their overflow behavior in generated C#;
  a maximum-key allocator overflow test verifies no reservation or saved row is published.
- The focused [MemoryGeneratedValues example](../../samples/ConfigurationExamples/MemoryGeneratedValues/README.md)
  links its designer to exact contracts, production source, test doubles and manifest. It demonstrates
  seed-aware keys, defaults and supplied values, save-time computed tokens, ordered batches, staged/shared
  saves, stale conflicts and rollback/baseline replay. The index now contains 29 applications,
  fourteen testing examples and twelve independently EF-free consumers.

See [fixture generated-value semantics](../implemented/testing/INMEMORY-GENERATED-VALUES.md). Shared contract cases compare
actual generated SQLite and fixture repositories, not handwritten references. Explicit factories are
testing policies, not translated SQL expressions: identity sequences, triggers, Unicode/collation,
database rowversion generation, cross-process races and other provider constraints are not inferred.
Additional value/provider policies, public-proxy write adaptation and graph/native simulation remain
separate extensions. Unselected options and ordinary production/contracts/API/cache shapes are unchanged.

## Generated-value verification (2026-10-08)

The Release solution build passed with zero warnings/errors. The full no-build/no-restore suite
passed **1,631 tests**: Tool 1,407, EntityFrameworkCore 66, Generator 10, Testing 146 and Attributes 2.
Twenty-four real-SQL-Server tests were skipped because no connection is configured; this is not
SQL Server runtime verification.

The 19 added cases cover explicit adapter prerequisites/removal/replacement/duplicates, ordinary-property,
capture/clock rejection, all output layouts, unchanged production/contracts/API/cache model shapes,
generated fixture compilation and capability receipts, maximum-key overflow, seeded/pending/supplied
keys, delayed once-per-save computed tokens, no-op/stale outcomes, all-store atomic failure, cancellation,
re-entry rejection, generated input-ordered batch DTOs, saved mapping failures, checkpoint/baseline replay
and typed partition-local allocation/saves. Generated-token deletion also works without unused factories.
Shared cases compare actual generated SQLite and fixture named Create/StageCreate/Rename/StageRename/
RenameMany/Delete/List operations, shared save counts and stored values; ASCII scalar demonstrations do
not imply universal Unicode, rowversion or identity equivalence.

All twenty-nine examples passed `-Check -Run -ValidateCompilation` with actual .NET 8 inputs,
independent contract/testing compilation and the approved Ledger baseline. MemoryGeneratedValues adds
four generated artifacts; all existing twenty-eight bundles regenerate unchanged. All twelve fake-only
dependency graphs exclude EF, Models, Designers, Design and Generation. Repository-local guide links,
generated-value source whitespace and `git diff --check` passed.

## Delivered mapped public-navigation read slice (2026-10-08)

F15.3 now combines explicit public scalar maps and declared relationships; F15.6 adds shared
SQLite cases and a focused RepositoryDesigner example:

- Existing optional `UseInMemoryTestQueries()`, `MapInMemoryEntity<TData>` and
  `HasInMemoryNavigation` declarations enable this combination. No extra global setting,
  service-side query API, default output or production/contracts/API/cache shape is introduced.
- Scalar initializer assignments remain separate from navigation declarations. Mapped rows gain
  plain collection/reference slots, so internal/private models and read-only/private-setter members
  can be described without constructing persistence entities or invoking their getters/setters.
  The fixture does not adapt domain behavior, custom collections or reverse write mappings.
- Each operation captures all participating public stores together, maps each source once to its
  canonical read rows, then loads selected edges by actual EF foreign/principal keys. Composite,
  alternate, optional, required and self-reference behavior retains the existing detached snapshot
  rules. Pages share one graph; cold streams capture at first move and retain it until disposal.
- Related mapped keys and accessed scalars must all be explicitly assigned. Missing declarations,
  maps, keys, fields or selected stores are RequiresOverride, even with empty rows. Strict generation
  requires an acknowledgement. Entity-shaped sources cannot receive mapped-target rows through
  original CLR navigation types; map both ends or script the operation.
- Include/ThenInclude/load-profile selectors name edges, not returned entity identity. Their
  loading boundaries are checked separately from scalar identity escape checks. Related mapped
  expressions receive full member coverage; an owner sort whitelist does not require unused owner
  properties on unrelated targets. Unsupported/unselected targets never emit dangling helper types.
- Global EF filters, AutoInclude, owned/shared/inherited models, custom key conversions/comparers,
  filtered loading and skip/many-to-many behavior remain conservative override/provider boundaries.
  Related visibility is never silently injected; child filtering remains in the shared designer.
- Manifest scalar `readMapping` receipts now optionally list plain navigation slots and target types.
  Scalar maps explicitly report no persistence construction. Per-operation `navigationSnapshot`
  receipts identify actual participants and ordered keys, with no implicit related policies or
  write adaptation.
- [MemoryProxyNavigation](../../samples/ConfigurationExamples/MemoryProxyNavigation/README.md) links
  its designer to four exact checked-in artifacts. Internal EF models with a read-only collection
  and private reference setters are arranged using immutable public seeds and consumed as nested
  DTOs without EF, Models, Designers or Generation dependencies. The index contains 30 applications,
  fifteen testing examples and thirteen independently EF-free consumers.

See [mapped public-navigation semantics](../implemented/testing/INMEMORY-PROXY-NAVIGATION.md) for configuration, loading
order and boundaries. Tooling also compiles private mapped graphs without a model-assembly
reference and compares named nested DTO operations with actual generated SQLite repositories.
These checks do not establish SQL collation, tracking, graph-write or concurrency equivalence.
Public-proxy writes and other provider/graph policies remain separate extensions.

## Latest mapped-navigation verification (2026-10-08)

The Release solution build passed with zero warnings/errors. The full no-build/no-restore suite
passed **1,655 tests**: Tool 1,431, EntityFrameworkCore 66, Generator 10, Testing 146 and Attributes 2.
Twenty-four real-SQL-Server tests were skipped because no connection is configured; this does not
establish SQL Server runtime verification.

The 24 added cases cover all output layouts, scalar/navigation manifests, unchanged disabled
production/contracts/API/cache shapes, private-model compilation without model references or entity
construction, missing maps/keys/scalars/declarations/targets, mixed row-type and identity boundaries,
strict overrides, EF filters/AutoInclude/comparer boundaries, unrelated owner sort fields, nested
composite/alternate/required/optional relationships, DTO/baseline isolation, self references,
pages/cold streams/cancellation/disposal, script precedence/reset, invalid keys and typed partitions.
An owned-target regression reproduces and fixes an unselected helper dependency: unrelated scalar
reads remain portable, related reads remain acknowledged scripts and generated fixtures compile.
Ten shared named-query invocations compare mapped fixture results with actual generated SQLite
repositories, including optional/missing results, related visibility, nested pages, From roots and
unfiltered loading; their read-only operations leave the real EF tracker empty.

All thirty examples passed `-Check -Run -ValidateCompilation` with actual .NET 8 inputs,
independent contract/testing compilation and the approved Ledger baseline. MemoryProxyNavigation
adds four generated artifacts; all previous twenty-nine bundles regenerate unchanged. All thirteen
fake-only dependency graphs exclude EF, Models, Designers, Design and Generation. All 77 repository
guides passed local-file link checks; mapped-navigation source whitespace and `git diff --check`
also passed.

## Delivered public scalar fixture write slice (2026-10-08)

F15.4 now supports explicit public-data writes for internal models; F15.6 adds the focused example
and shared actual-generated SQLite contract cases:

- `MapInMemoryWrites<TData>((candidate, saved) => ...)` requires a matching public store and complete
  scalar read map. It uses generated scalar slots, never the persistence constructor, default property
  initializers or setter behavior. It does not infer a reverse entity-to-DTO transformation.
- Complete ordinary CLR scalar coverage includes keys, tokens and unassigned columns. Primary aliases
  must be direct same-type public-data members in EF key order; compound keys are explicitly supported.
  Creates assign all ordinary scalars except those covered by explicit generated-value factories.
- Public immutable arrangement rows retain Seed/Find/Snapshot/baseline APIs. An internal command view
  shares their backing identity and rejects bypass writes through the original public-data store.
  Encoders receive current detached saved metadata for updates, null for creates, and never run for
  untouched/deleted rows. Key/copy/full scalar round-trip validation rejects lossy encoders.
- All stores finish scalar/generated-value/public encoding preparation before any commit. Failure,
  conflict or pre-commit cancellation preserves shared atomicity. Earlier unrelated staging survives
  a failing saved command. Selected context partitions isolate writes and generated reservations.
- Existing designed guards, visibility, expected versions, permissions, approved assignments, named
  results, Stage/Save, bounded PatchMany/DeleteMany and checkpoint/baseline reset semantics are reused.
  Tracked deletes with dependent relationships now require explicit overrides rather than pretending
  to perform cascades/fixup. Navigation/graph/native writes remain outside this scalar simulation.
- Explicit generated-value factories work with mapped rows and inferred same-store scalar LINQ.
  Registered type-adapter markers expand in scalar read/write/value declarations. Private model casts,
  ambient state and whole-entity behavior are rejected before model names can leak into testing output.
- Commands disabled or entity `WriteEnabled = false` emit no writer/value registrations. Reads can be
  disabled independently while mapped commands retain needed scalar helpers. Removing a write map
  retains read/storage/scripts and removes its dependent factories. Removing the read map/store also
  removes dependent writes. All global testing switches stay off by default.
- Testing-only manifest receipts describe the explicit write source, saved-data rules and pre-commit
  scalar/key validation, with no persistence construction, inferred reverse projection, navigation
  writes or claimed relational equivalence. Production/contracts/API/cache shapes remain unchanged.
- [MemoryProxyWrites](../../samples/ConfigurationExamples/MemoryProxyWrites/README.md) maps its
  [ProxyNotebookRepositoryDesigner](../../samples/ConfigurationExamples/MemoryProxyWrites/Configuration/ProxyNotebookRepositoryDesigner.cs)
  to four exact artifacts. Two internal persistence types use immutable public rows and generated
  CQRS interfaces in an independently EF/model-free executable. The index contains 31 applications,
  sixteen testing examples and fourteen independently EF-free consumers.

See [the public write adapter contract](../implemented/testing/INMEMORY-PROXY-WRITES.md). These checks establish suitable
scalar fixture behavior, not SQL identity/rowversion/races, graph persistence, custom provider
conversion or database constraints. Additional value/provider policies and graph/native simulation
remain extensions; ordinary/default output and unselected example bundles stay unchanged.

## Latest mapped-write verification (2026-10-08)

The Release solution build passed with zero warnings/errors. The complete no-build/no-restore suite
passed **1,694 tests**: Tool 1,460, EntityFrameworkCore 66, Generator 10, Testing 156 and Attributes 2.
Twenty-four real-SQL-Server tests were skipped because no connection is configured; these results
do not establish SQL Server runtime verification.

The 39 new cases cover all output layouts, EF/model-free compilation, unchanged production/API/cache
shapes, independently disabled reads/commands/entity writes, removal/prerequisite/coverage diagnostics,
registered adapter expansion, internal-model construction avoidance, shared and keyed pending saves,
generated key reservations/defaults/computed tokens, compound keys and ordered batches. Runtime cases
cover null/key/scalar/throwing encoder failures, preservation of earlier staging, latest saved metadata,
untouched/deleted rows, baseline/checkpoint reset, cancellation during encoding and context re-entry.
Shared named-command cases compare actual generated SQLite operations rather than handwritten
reference implementations. Scalar adapters do not silently simulate dependent relationship deletion.

Sized array expressions now preserve bounds instead of becoming initializers. Mapped Create emission
also handles an explicit null reference sentinel before a configured insert factory, preserving the
nullable suppression that expression trees cannot represent. The keyed fixture case compiles cleanly.

All thirty-one examples passed `-Check -Run -ValidateCompilation` with actual .NET 8 inputs,
independent contract/testing compilation and the approved Ledger baseline. MemoryProxyWrites adds
four generated artifacts; all existing thirty bundles regenerate unchanged. All fourteen fake-only
dependency graphs exclude EF, Models, Designers, Design, Generation, Tool and Validation. All 79
repository guides pass local-file link checks; mapped-write source whitespace and `git diff --check`
also passed.

## Delivered fixture-key identity slice (2026-10-09)

F15.2/IM04 now expose the runtime's comparer support through an explicit designer setting:

- `HasInMemoryKeyComparer<TKey>(() => ...)` requires the exact existing store key type. Ordinal string
  comparers, explicit EqualityComparer defaults and independent public constructor expressions are
  emitted and evaluated once per store/context partition. Generation never invokes factories or arbitrary getters.
- RGD3204 rejects missing/mismatched stores, captured/null/ambient/private/model/EF construction and
  cast escapes. Disabled/unselected stores ignore unused factories. Removing a comparer retains all
  other adapters; replacing/removing the store clears it. All testing remains optional/off by default.
- Configured identity flows through entity/public stores, mapped command views, duplicate seed/create
  checks, pending/saved dictionaries, baselines, snapshots and checkpoint restores. Defaults retain
  byte-identical generation; production/contracts and existing example bundles remain unchanged.
- Predicates, sorting, visibility, joins/navigation/batch matching, scalar/version/round-trip checks
  and context partition comparison are not rewritten. This is not database collation/constraint parity.
  Consumers own immutable keys and deterministic, stable custom equality/hash implementations.
- Testing-only receipts state FixtureKeyIdentityOnly and explicitly deny query/relationship/context-key
  changes and relational equivalence. No new production contract, output category or CLI DSL is added.
- [MemoryKeyIdentity](../../samples/ConfigurationExamples/MemoryKeyIdentity/README.md) maps its small
  [KeyDirectoryRepositoryDesigner](../../samples/ConfigurationExamples/MemoryKeyIdentity/Configuration/KeyDirectoryRepositoryDesigner.cs)
  to four exact artifacts. Its model-free consumer checks identity versus predicates, duplicate policies,
  mapped Stage/Save and baseline/checkpoint restore. There are now 32 applications, seventeen testing
  examples and fifteen EF-free testing consumers.

See [fixture-key comparer rules](../implemented/testing/INMEMORY-KEY-COMPARERS.md). Additional value/provider policies and
graph/native write simulation remain extensions.

## Latest fixture-key verification (2026-10-09)

The Release solution build passed with zero warnings/errors. The complete no-build/no-restore suite
passed **1,724 tests**: Tool 1,490, EntityFrameworkCore 66, Generator 10, Testing 156 and Attributes 2.
Twenty-four real-SQL-Server tests were skipped because no connection is configured; these results
do not establish SQL Server runtime verification.

The 30 new cases cover all output layouts, independent EF/model-free compilation, unchanged production
contracts/API snapshots/default outputs, exact-key prerequisites/removal, explicit defaults, unused
factories and rejection of captured/null/culture/static/method/private/model/generic-or-derived-EF
construction and inaccessible constructors. Runtime cases verify custom construction only at fixture
creation, once per store/partition, plus composite keys, read-only stores, duplicate seed policies,
atomic range arrangement, pending and save-time duplicate failures, mapped saves, baseline/checkpoint
restore and unchanged query/command/batch predicates. Store comparers do not merge context partitions.

All thirty-two examples passed `-Check -Run -ValidateCompilation` with actual .NET 8 inputs and
independent contract/testing compilation. The latest rebuilt runner's follow-up `--check` also passed
with the approved Ledger API baseline. MemoryKeyIdentity adds four generated artifacts; the prior
thirty-one bundles regenerate unchanged. All fifteen fake-only dependency graphs exclude EF, Models,
Designers, Design, Generation, Tool and Validation. All 81 repository guides pass local-file link
checks; comparer/example/test source whitespace and `git diff --check` also passed.

## Delivered immediate fixture-write slice (2026-10-09)

F15.4/IM10 now support explicit, per-command `SimulateImmediateWrites(bool)`:

- Suitable scalar Update/Delete, count-only, WithOutcome and ReturnsMany/consume commands reuse the
  production designer's guards, visibility/bypasses, predicates, original-row setters, explicit input
  versions, context keys and DTO mappings. This option changes no production contract/API/cache shape.
- Selection uses saved rows, not pending tracked values. Matching unchanged updates count. Update
  results use post-update state; Delete returns removed scalars. Zero-row outcomes remain NotMatched,
  not guessed absence/conflict. Explicit ordinary scalar/binary expected tokens use structural equality.
- Setter/value/copy/key/encoder/result preparation and cancellation complete before any target commits.
  Throwing returned DTO constructors cannot partly consume/update rows. Explicit update-generated
  factories also run, without inferring SQL computed expressions, triggers or database rowversions.
- Existing staging is neither flushed nor refreshed. Later shared saves can merge approved assignments
  or fail on original-token/missing-row conflicts, retaining pending entries. Checkpoint rollback and
  baseline restore include immediate changes; no SQL locking/isolation/race guarantees are claimed.
- Entity-shaped, complete public-mapped and compound-key stores are supported. Typed overrides still
  take precedence. Strict unsupported commands retain RGD3402; graph/cascade/native-upsert, active EF
  filters, navigation writes and custom/provider behavior cannot bypass the coverage boundary.
- Per-operation `immediateSimulation` receipts explicitly document pending isolation, pre-commit
  preparation, unspecified return order and false relational equivalence. Ordinary/default output and
  existing sample bundles stay unchanged; all testing remains optional/off by default.
- [MemoryImmediateWrites](../../samples/ConfigurationExamples/MemoryImmediateWrites/README.md) links
  [DispatchQueueRepositoryDesigner](../../samples/ConfigurationExamples/MemoryImmediateWrites/Configuration/DispatchQueueRepositoryDesigner.cs)
  to four exact generated artifacts and an EF/model-free consumer. There are now 33 applications,
  eighteen testing examples and sixteen fake-only consumers.

See [immediate fixture semantics](../implemented/testing/INMEMORY-IMMEDIATE-WRITES.md). Additional generated-value/provider
policies, graph reconciliation and native insert/upsert simulation remain extensions.

## Latest immediate-write verification (2026-10-09)

The Release solution build passed with zero warnings/errors. The complete no-build/no-restore suite
passed **1,775 tests**: Tool 1,522, EntityFrameworkCore 66, Generator 10, Testing 175 and Attributes 2.
Twenty-four real-SQL-Server tests were skipped because no connection is configured; these results
do not establish SQL Server runtime verification.

The 51 new cases cover opt-in/default behavior, all output layouts, independent EF/model-free
compilation, public scalar mappings and compound keys, prerequisites and strict coverage diagnostics,
guards/visibility, scalar/binary versions, original-row assignments, count/outcome/returned results,
explicit generated-value factories and context partitions. Runtime cases cover preservation of
staging, later save conflicts, script precedence/reset, baseline/checkpoint rollback, setter/encoder/
returned-constructor failures, cancellation, context ownership and re-entry. Shared cases compare
actual generated SQLite operations with entity-shaped and mapped fixtures; failed result construction
rolls back immediate changes while retaining earlier staging.

All thirty-three examples passed `-Check -Run -ValidateCompilation` with actual .NET 8 inputs,
independent contract/testing compilation and the approved Ledger API baseline. MemoryImmediateWrites
adds four generated artifacts; all existing thirty-two bundles regenerate unchanged. All sixteen
fake-only dependency graphs exclude EF, Models, Designers, Design, Generation, Tool, Validation and
relational provider dependencies. All 84 repository Markdown guides pass local-file link checks;
immediate-write source/example whitespace and `git diff --check` also passed.

## Delivered saved-state insert/upsert fixture slice (2026-10-09)

F15.4/IM08-IM10 extend the existing per-command `SimulateImmediateWrites(bool)` opt-in to suitable
scalar `InsertIfAbsent` and conditional `InsertOrUpdate` definitions:

- Conflict targets reuse production validation: supplied non-null primary/alternate/unique-index
  scalars, including composite targets. Hidden saved rows still cause conflicts; designer visibility,
  guards and scalar/binary expected versions constrain only the existing-row update branch.
- Outcomes are Inserted/Updated/AlreadyExists/NotMatched, with configured detached results only on
  success. Updates preserve the saved key/target and all setters read the original saved row.
- Native insertion requires complete scalar inputs or explicit insert-timed factories, for both
  entity-shaped and public-mapped stores. CLR/database defaults are not inferred. Update-only
  factories cannot silently supply omitted insert columns. Internal mapped models are not constructed.
- Insert factories see saved same-store rows, never pending reservations; update factories run after
  ordinary assignments. Result constructors, public encoders, factories, copies, key/target checks
  and cancellation finish before commit. Failure retains all saved rows and earlier staging.
- Staging is never flushed/refreshed. Later shared saves can merge approved unrelated values, detect
  stale tokens or collide with an immediately inserted primary key. Checkpoints/baselines include
  saved insert/update state; typed context partitions isolate conflicts and scalar allocation.
- Explicit overrides win; non-strict uncovered calls still fail. Production contracts/API/cache
  shapes and all existing example bundles remain unchanged. All fixture features stay off by default.
- `atomicSimulation` receipts describe saved conflict probes, update-only visibility, branch outcomes,
  saved-only factory inputs, pending isolation and false relational equivalence. SQL sequence allocation,
  constraints, collation, locks/races, triggers, observers and external effects are not simulated.
- [MemoryAtomicWrites](../../samples/ConfigurationExamples/MemoryAtomicWrites/README.md) maps
  [ReservationDirectoryRepositoryDesigner](../../samples/ConfigurationExamples/MemoryAtomicWrites/Configuration/ReservationDirectoryRepositoryDesigner.cs)
  to four exact artifacts. It uses public immutable fixture rows and generated CQRS ports in an
  independently EF/model/provider-free consumer. There are now 34 applications, nineteen testing
  examples and seventeen fake-only consumers.

See [the saved-state insert/upsert contract](../implemented/testing/INMEMORY-ATOMIC-WRITES.md). Exact shared fake/SQLite
cases supply immutable IDs: SQLite allocation can advance on conflict/no-op attempts, unlike a
fixture's explicitly declared Max + 1. Separate factory/partition/reservation tests establish only
fixture behavior. Additional generated-value/provider policies and graph reconciliation remain extensions.

## Latest saved-state insert/upsert verification (2026-10-09)

The Release solution build passed with zero warnings/errors. The complete no-build/no-restore suite
passed **1,829 tests**: Tool 1,551, EntityFrameworkCore 66, Generator 10, Testing 200 and Attributes 2.
Twenty-four real-SQL-Server tests were skipped because no connection is configured; these results
do not establish SQL Server runtime verification.

The 54 new cases cover all output layouts, unchanged production/API shapes, per-command/global opt-outs,
strict/scripted/non-strict coverage, production-writer prerequisites, complete insert mappings and
insert-versus-update factory timing. They also cover entity/public/compound-key stores, composite
conflicts, hidden conflicts, scalar/binary expected versions, original-row setters, returned/no-value
outcomes, partition isolation and script precedence/reset. Runtime cases include duplicate-target and
primary-key failures, saved-only allocation versus pending reservations, immutable keys/targets,
factory/setter/encoder/result failures, cancellation, ownership/re-entry, baseline/checkpoint restore
and retention of earlier staging. Actual generated SQLite comparisons exercise suitable named commands,
rollback and later tracked-save conflicts; four result-constructor failure cases preserve earlier
staging on both real and fake implementations. Exact comparisons deliberately supply immutable IDs.

All thirty-four examples passed `-Check -Run -ValidateCompilation` with actual .NET 8 inputs,
independent contract/testing compilation and the approved Ledger API baseline. MemoryAtomicWrites
adds four generated artifacts; the existing thirty-three bundles regenerate unchanged. All seventeen
fake-only dependency graphs exclude EF, Models, Designers, Design, Generation, Tool, Validation and
relational provider dependencies. All 86 repository Markdown guides pass local-file link checks;
33 feature source/example files pass whitespace checks, and `git diff --check` also passed.
The rebuilt generator's final 61 focused immediate/insert/upsert regressions also passed. Its final
bootstrap-input `--check --compile-references` passed for every bundle after clarifying the manifest's
tracked-reservation versus immediate saved-only allocation receipt; generated C# remained unchanged.

## Delivered bounded child-store fixture-write slice

- Per-command `SimulateCollectionWrites(bool)` is independently optional/off. It requires automatic
  fixture commands, designed tracked leaf-child reconciliation and selected writable parent/child stores.
  Unsupported mappings retain RGD3402/explicit scripts rather than claiming graph persistence.
- Reuses the production collection input, matching, scalar add/update, missing-item, limit, condition
  and expected-version definitions. Complete mapped ownership FKs come from the selected parent,
  including composite principals; fixture-key comparers do not rewrite collection matching.
- Separate saved child tables, visible-only mutation scope and conservative owner-scoped pending-target
  rejection. Hidden and unrelated rows are not inferred as delete candidates or moved by input keys.
- Explicit public storage/read/write maps adapt internal models without constructing entities or using
  seeded navigation graphs. Child insert values require full scalar mappings or explicit factories;
  new IDs reserve earlier pending additions. Existing generated IDs need no factory for update/delete-only rules.
- Parent/child scalar staging composes across multiple collections. Later validation/setter/factory/
  cancellation/save failure restores command changes while preserving prior unrelated staging. Child
  conflicts return `Conflict`; unrelated shared-save conflicts remain `RepositoryWriteConflictException`.
- Saves include the shared fixture unit of work and return scalar parent projections, including explicitly
  computed save-time values. Post-save projection failures remain committed-result exceptions; existing
  checkpoint/baseline restoration covers all stores. No SQL constraints, triggers, locks or race parity is implied.
- Conditional manifest receipts identify each supported command's child stores, limits, policies,
  ownership, staging/save and scalar-only result boundary. Unselected production/cache/API output is unchanged.
- [MemoryCollections](../../samples/ConfigurationExamples/MemoryCollections/README.md) maps
  [ChecklistWorkspaceRepositoryDesigner](../../samples/ConfigurationExamples/MemoryCollections/Configuration/ChecklistWorkspaceRepositoryDesigner.cs)
  to its four actual artifacts and an independently EF/model/provider-free consumer. The index now has
  35 applications, twenty testing examples and eighteen fake-only consumers.

See [the child-store simulation contract](../implemented/testing/INMEMORY-COLLECTIONS.md). The 48 focused cases (30 generation/
execution/contract and 18 runtime cases) cover every output layout, default opt-out, scalar-map/child-store
prerequisites and excluded child CLR types, uninferred defaults, version/visibility/ownership, composite FK binding, optional/null/empty
inputs, ordered multiple-addition reservations, shared saves, late rollback, cancellation, script overrides
and deliberately unsupported graph results. Shared cases execute actual generated SQLite and fixture
repositories; SQL identity allocation after deletions is deliberately not equated to row-derived test IDs.

Verification for this slice: Release solution build passed with zero warnings/errors. The complete
Release/no-restore suite passed **1,877 tests**: 1,581 Tool, 66 EF integration, 10 attribute-generator,
218 Testing and 2 attribute tests. All 24 SQL Server integration cases were deliberately skipped because
the opt-in connection is unavailable; this run does not claim SQL Server verification.

All thirty-five examples passed `-Check -Run -ValidateCompilation`, including actual .NET 8 contract/
testing references and the approved Ledger API baseline. MemoryCollections adds four generated
artifacts; all prior thirty-four bundles regenerate unchanged. All eighteen fake-only dependency
graphs exclude EF, Models, Designers, Design, Generation, Tool, Validation and relational providers.
The repository's Markdown local-file links and tracked/untracked feature whitespace checks passed.

## Delivered fresh saved command-result slice

- `ReturnsSaved<TResult>(projection)` and its request-bound overload are explicit/optional. Ordinary
  `Returns` stays a direct saved-row mapper; the new mode is selected only on single tracked
  Create/Patch + Save, including supported child reconciliation. No simple output or default is expanded.
- Production projects an untracked query of the complete saved primary key, rather than a tracked
  navigation graph or the mutation predicate (which may now be false). Async result callbacks run
  after the shared save and inside the selected context/timeout/transaction lifetime.
- Root policies are rechecked unless explicitly bypassed. Related visibility is authored in the
  projection, not silently injected. Scalar/DTO projections and nested reporting collections use
  normal EF translation; this separate read is not atomic OUTPUT/RETURNING or a concurrency guarantee.
- Optional test commands reuse saved tables, pure public scalar maps and the existing declared
  navigation-read subset. Participating detached snapshots are captured together after shared save;
  newly inserted/generated rows, edits, deletions and unrelated saved staging are visible in results.
- Missing ownership/version/no-change outcomes never query a result. Post-save result failures,
  including cancellation or invalid required fixture relationships, remain
  `RepositorySavedResultException`, not rollback/silent success. Explicit surrounding transaction/
  checkpoint rollback can still restore the saved writes. Typed scripts still override automatic handlers.
- RGD3701 rejects Stage/Delete/batches/immediate writes, missing explicit Save, redundant reload
  and incomplete/non-readable CLR primary keys. RGD3402 continues to describe unsupported fixture
  relationships and mutation predicates; saved-result reads do not expand navigation-dependent writes.
- Conditional `savedResultQuery` receipts document keys, snapshot scope, visibility, relationships,
  committed-result failure and excluded provider guarantees. New fields are omitted when unused;
  the previous thirty-five example bundles regenerate unchanged.
- [MemorySavedResults](../../samples/ConfigurationExamples/MemorySavedResults/README.md) pairs one
  [SavedNotebookRepositoryDesigner](../../samples/ConfigurationExamples/MemorySavedResults/Configuration/SavedNotebookRepositoryDesigner.cs)
  with its four generated artifacts and EF/model/generator-free consumer. It demonstrates scalar
  rename/shared save, child reconciliation, created references, explicit archived-row visibility,
  mapped public rows, generated IDs and checkpoint/baseline restoration. The index now has 36
  applications, twenty-one testing examples and nineteen fake-only consumers.

See [saved result semantics and boundaries](../implemented/writes/SAVED-COMMAND-RESULTS.md). Thirty-nine new cases cover
all layouts, backend opt-out/unchanged production API, strict/scripted coverage, lifecycle guards,
scalar/nested/request results, complete composite keys with public maps, shared save, generated create
results, result failure/cancellation and explicit rollback. Shared cases execute actual generated
SQLite and fixture repositories against the same designer; eight additional async executor cases
verify the save-versus-result boundary. Real SQL/provider constraints and races are not inferred.

The complete Release/no-restore suite passed **1,916 tests**: 1,612 Tool, 74 EF integration,
10 attribute-generator, 218 Testing and 2 attribute tests. Twenty-four opt-in SQL Server cases
were skipped because no connection is configured; no SQL Server runtime verification is claimed.

The complete Release solution build passed with zero warnings and errors. All 36 example bundles
regenerated, independently compiled for .NET 8 and ran successfully; the final generation-freshness
check and approved Ledger API baseline check passed. The previous thirty-five bundles stayed unchanged.
All nineteen fake-only dependency graphs exclude EF, Models, Designers, Design, Generation, Tool,
Validation and relational providers. The Markdown audit checked 90 guides and 1,449 local links with
no broken targets; feature-file and tracked-diff whitespace checks passed.

## Delivered fresh saved batch-result slice

- Optional `PatchMany(...).Save().ReturnsSaved(...)` extends the existing designer result mode;
  ordinary `Returns`, default output and native `ReturnsMany` retain their meanings. Stage/DeleteMany
  remain excluded. No new mandatory flag, service-side projection DSL or testing dependency is added.
- Production creates one untracked result query for the whole saved batch, using every primary-key
  part and a balanced generated comparison predicate. It does not reapply the changed mutation
  predicate or issue one result query per item. Provider limits/translation/splitting remain explicit.
- Scalar/nested/request-bound DTOs are projected by EF; complete structural key correlation returns
  exactly one value per input, in input order. Missing/hidden/duplicate results cannot silently shorten
  a batch. Root policies are rechecked unless bypassed; related visibility stays explicit in projection.
- One asynchronous batch result callback runs after shared Save. Result failure/cancellation or an
  invalid result count raises `RepositorySavedResultException`, preserving the saved write. Existing
  explicit transaction rollback still works; saving is not an atomic returning/isolation guarantee.
- Optional in-memory commands run one saved-result callback outside the write lock, capture participating
  tables together and load the existing declared relationship subset. Public maps, complete composite
  keys, shared staged writes, explicit generated values, input order, script overrides and checkpoint
  restoration retain their designed behavior. Scalar saved results need no query backend.
- Supported batch receipts append `batchOrder` and `batchCapture` to `savedResultQuery`; existing
  single-row receipts and unselected bundles are unchanged. RGD3701 now accepts saving PatchMany but
  retains lifecycle/readable-key guards; normal RGD3001/RGD3002 and strict RGD3402 restrictions remain.
- [MemoryBatchResults](../../samples/ConfigurationExamples/MemoryBatchResults/README.md) maps
  [BatchNotebookRepositoryDesigner](../../samples/ConfigurationExamples/MemoryBatchResults/Configuration/BatchNotebookRepositoryDesigner.cs)
  to four actual artifacts and an EF/model/generator-free consumer. It covers request order,
  equal local IDs in composite keys, fresh related tables after shared save, explicit archived-row
  visibility, public arrangement maps and checkpoint rollback. The index has 37 applications,
  twenty-two testing examples and twenty fake-only consumers.

See [saved batch results and boundaries](../implemented/writes/SAVED-BATCH-RESULTS.md). Forty-seven new cases cover
generation layouts/default opt-outs/scalar-read opt-out, lifecycle and portability guards, ordered
composite/nested/request results, shared save, missing/hidden/stale targets, unassigned/empty input,
script overrides, structural binary correlation, missing/duplicate result keys, callback cardinality,
post-save cancellation/failure and explicit rollback. Actual generated SQLite/fixture contract cases
share one designer; SQL capture confirms one target query plus one nested result query for two items.
Provider SQL isolation/constraints/races and arbitrary graph writes are not inferred.

The complete Release/no-build/no-restore suite passed **1,963 tests**: 1,636 Tool, 85 EF integration,
10 attribute-generator, 230 Testing and 2 attribute tests. The new coverage adds 24 Tool, 11 EF
runtime and 12 Testing runtime cases. Twenty-four opt-in SQL Server cases were skipped because no
connection is configured; this run makes no SQL Server runtime verification claim.

Verification: the Release solution build passed with zero warnings/errors. All thirty-seven examples
regenerated, independently compiled for .NET 8 and ran successfully. The final freshness check and
approved Ledger API baseline passed; all previous thirty-six bundles regenerated unchanged. The
twenty fake-only consumer dependency graphs exclude EF, Models, Designers, Design, Generation, Tool,
Validation and relational providers. Markdown local-link and feature/tracked whitespace audits passed.

## Delivered explicit many-to-many fixture-read slice

- Existing optional `HasInMemoryNavigation` now lowers suitable skip-navigation reads through an
  explicit ordinary CLR join entity. Both endpoints and the join need selected stores; every
  referenced principal/foreign key needs public readability or an explicit scalar map. There is
  no new global switch, duplicate relationship-key DSL, mandatory dependency or production change.
- EF supplies both foreign keys, their directions and complete principal keys. Composite/alternate
  keys and different endpoint key arities retain their declared component order. Both join foreign
  keys must be required; implicit/shared/owned joins and optional/null projection semantics retain
  RequiresOverride rather than silently claiming SQL equivalence.
- One coordinated detached snapshot captures the participating tables. Typed indexed loading runs
  outside the fixture lock, builds separate related lists in join snapshot order, validates unique
  principals/link pairs and required references before assigning an edge, and observes cancellation.
  No reflection, entity construction, inferred inverses or seeded navigation pointers are used.
- Public scalar endpoint/join maps emit plain relationship slots even for internal/read-only models.
  Only accessed/loaded declared edges are rebuilt. Root policies stay on roots, not implicitly on
  endpoint/join relationships; intended child filtering remains in the shared designer expression.
  Provider filters/comparers, AutoInclude, filtered includes and entity identity retain overrides.
- Nested DTOs, predicates, unfiltered chains, explicit From access, pages and cold streams reuse this
  snapshot. Single/batch ReturnsSaved can read these relationships after shared save; invalid result
  graphs fail after saving without undoing scalar writes. Many-to-many mutation/reconciliation is
  not added. Existing script precedence, reset/baseline and fixture partitions remain in place.
- Per-edge `navigationSnapshot` receipts add join store, foreign keys, requiredness, pair validation,
  order and policy boundaries only for selected many-to-many edges. Saved result receipts add
  `joinRelationships` only when used; ordinary edge receipts and unrelated bundles stay unchanged.
- [MemoryManyToMany](../../samples/ConfigurationExamples/MemoryManyToMany/README.md) maps
  [TaggedArticleRepositoryDesigner](../../samples/ConfigurationExamples/MemoryManyToMany/Configuration/TaggedArticleRepositoryDesigner.cs)
  to four artifacts and an EF/model/generator-free consumer. It demonstrates shared tags, complete
  composite joins, accessed inverses, explicit visibility, detached nested DTOs and baseline restore.
  The current index has 38 applications, twenty-three testing examples and twenty-one fake-only consumers.

See [many-to-many semantics and boundaries](../implemented/testing/INMEMORY-MANY-TO-MANY.md). New generation/execution
tests cover all layouts and both store shapes, explicit selection/mapping/key/declaration guards,
filter/comparer/AutoInclude/optional-join boundaries, opt-outs, empty/shared/hidden relationships,
pages/streams, scripts, saved single/batch DTOs and committed-result failure. Runtime cases cover
structural/composite keys, independent arities, uniqueness/required references, getter failures,
self edges and cancellation through the last callback. Shared contracts run the same actual generated
operations on SQLite and both fixture shapes; nullable-join SQL differences informed the explicit boundary.

The complete Release/no-build/no-restore suite passed **2,013 tests**: 1,664 Tool, 85 EF integration,
10 attribute-generator, 252 Testing and 2 attribute tests. This slice adds 50 cases (28 Tool and
22 Testing runtime). Twenty-four opt-in SQL Server cases were skipped because no connection is
configured; no SQL Server runtime verification is claimed.

Verification: the complete Release solution build passed with zero warnings/errors. All thirty-eight
examples regenerated, independently compiled against their actual .NET 8 inputs and ran successfully.
The final generation-freshness and approved Ledger API baseline checks passed. Hash comparison confirms
all 143 previous generated artifacts remain unchanged; the new example adds exactly four artifacts.
All twenty-one fake-only consumer dependency graphs exclude EF, Models, Designers, Design, Generation,
Tool, Validation and relational providers. Markdown local-link and feature/tracked whitespace audits passed.

## Delivered composite child-matching slice (2026-10-11)

- Existing `ReconcileCollection` now accepts an explicit two-to-seven-component `ValueTuple`
  matching key. No new global switch or service-side predicate DSL is needed; scalar-key commands,
  compatibility defaults and unconfigured fixture commands stay unchanged.
- Generation validates distinct direct mapped non-null scalar components and a key or unfiltered
  unique index proving uniqueness globally or within the complete owner foreign key. Stricter unique
  subsets containing a matching column suffice; unmatched extra columns, filtered/non-unique indexes,
  arrays, computed selectors, nested tuples (including scalar-converted or required nullable CLR
  tuples) and custom key constructors do not.
- Add must explicitly supply every application-generated matching component. Update cannot change
  any component, even with `AllowWrite`. EF ownership binding, leaf-graph limits, protected assignments,
  parent/child expected versions, visibility and explicit missing-item policies remain in force.
- Typed generated checks evaluate the tuple once and reject any null component. Duplicate complete
  input keys fail before context acquisition or fixture selection; invalid saved keys fail before
  child mutation and restore command-assigned parent scalars. Matching uses default CLR tuple equality,
  not database collation or fixture identity comparers.
- Optional `SimulateCollectionWrites()` reuses the same matching delegates and existing selected
  entity/public-data stores. Both fixture shapes retain Stage/shared Save, scripts, cancellation,
  baseline restore and explicit checkpoint rollback; no SQL constraint or isolation equivalence is
  claimed. Provider conversions/comparers retain explicit override coverage.
- Only configured composite collections add a `compositeMatchingKey` manifest receipt: ordered
  properties, equality, null handling, owner/visible scope and immutability. No extra receipt or key
  helper is emitted for scalar matching.
- [CompositeCollections](../../samples/ConfigurationExamples/CompositeCollections/README.md) links
  [TranslationCatalogRepositoryDesigner](../../samples/ConfigurationExamples/CompositeCollections/Configuration/TranslationCatalogRepositoryDesigner.cs)
  to exactly four artifacts. Its EF/model/generator-free consumer demonstrates equal key prefixes,
  complete composite ownership, hidden-row preservation, public arrangement rows, generated child
  IDs, versions, Stage/shared Save, null-key rejection and checkpoint rollback. The current index has
  thirty-nine applications, twenty-four testing examples and twenty-two fake-only consumers.

See [composite matching semantics and boundaries](../implemented/writes/COMPOSITE-COLLECTION-KEYS.md). New tests cover
all supported tuple arities, output layouts and store shapes; uniqueness/component/create/update
diagnostics; provider override boundaries; scalar/fixture opt-outs; complete owner and visible scope;
limits, null/duplicate keys, conflicts, scripts, unrelated pending changes and Stage/shared Save.
Shared contracts run actual generated production operations on SQLite and both fixture shapes.
Runtime cases cover every null-component position, nullable value components, single evaluation
and default tuple equality. Broader graph mutation remains outside this slice.

The final complete Release/no-build/no-restore suite passed **2,100 tests**: 1,716 Tool,
85 EF integration, 10 attribute-generator, 287 Testing and 2 attribute tests. This slice adds
87 cases (52 Tool and 35 Testing runtime). Twenty-four opt-in SQL Server cases were skipped
because no connection is configured; no SQL Server runtime verification is claimed.

A default-parallel rerun exhausted memory in compiler metadata allocation. The successful final
retry bounded test concurrency to two threads with the
[xUnit RunSettings option](https://xunit.net/docs/config-runsettings), without changing the
implementation or global test configuration:

```powershell
dotnet test RonSijm.RepoGen.slnx -c Release --no-build --no-restore --disable-build-servers -m:1 --logger 'console;verbosity=minimal' -- xUnit.MaxParallelThreads=2
```

Verification: the complete Release solution build passed with zero warnings/errors. All thirty-nine
examples regenerated, independently compiled against their actual .NET 8 inputs and ran successfully.
The final generation-freshness and approved Ledger API baseline checks passed. Hash comparison confirms
all 149 previous generated files/inventories remain unchanged; the new example adds exactly four artifacts.
All twenty-two fake-only consumer dependency graphs exclude EF, Models, Designers, Design, Generation,
Tool, Validation and relational providers. Markdown local-link and feature/tracked whitespace audits passed.

## Bounded designer-defined creation (2026-10-11)

Delivered the optional F09/F15.4/IM08-IM09 create-batch slice:

- `HasCommand(...).CreateMany<TItem, TRequest>(items, mapping, maximumItems)` defines one closed
  scalar-creation operation. No synthetic selection key or service-side mapper is required.
  The default bound is 1,000; no declaration means no new API/output. Explicit named commands
  remain available with simple defaults even when generic range helpers are disabled.
- Null/oversized inputs and cancellation fail before context/fixture acquisition; enumeration is
  bounded and performed once. Empty input returns `NoChanges` without flushing earlier pending work.
  Every candidate is prepared before any row in that batch is attached/staged.
- Explicit Stage/shared Save, command-local protected writes, input-ordered scalar IDs/DTOs and
  `ReturnsSaved` are supported. Fresh results reuse one complete-key query and existing visibility
  rules. A saved-result create may deliberately bypass a root policy for the result read, not authorize
  or select insertion.
- Production uses tracked EF creation and one shared save, not native bulk insertion. Failure cleanup
  preserves earlier pending writes, detects reverted generated keys before detachment and removes
  new relationship-fixup references. SQLite regression cases verify retries with automatic detection
  enabled and disabled. Post-save result failures keep explicit already-saved exception semantics.
  Relational transaction/savepoint guards are shared with patch batches.
- Optional `UseInMemoryTestCommands` lowers entity-shaped and explicit public-data stores. Pure
  generated-value factories see saved rows, earlier pending reservations and earlier candidates
  without partial staging. Shared saves, saved snapshots, scripts, reset and checkpoint semantics
  remain; database constraints/provider identities and persistence constructors are not inferred.
- Only configured operations receive a create-batch receipt. Matching-key PatchMany/DeleteMany
  models remain separate; existing contracts and generation receipts are unchanged.
- [MemoryCreateBatches](../../samples/ConfigurationExamples/MemoryCreateBatches/README.md) links
  [IntakeCatalogRepositoryDesigner](../../samples/ConfigurationExamples/MemoryCreateBatches/Configuration/IntakeCatalogRepositoryDesigner.cs)
  to exactly four outputs. Its EF/model/designer/generator-free consumer demonstrates reserved IDs,
  fresh related DTOs in input order, preparation failure, Stage/shared Save and rollback. The index
  now has forty applications, twenty-five testing examples and twenty-three fake-only consumers.

See [creation semantics and boundaries](../implemented/writes/CREATE-MANY-COMMANDS.md). The complete Release solution
build passed with zero warnings/errors. All forty examples regenerated, independently compiled
against actual .NET 8 inputs and ran successfully. Freshness and the approved Ledger API baseline
checks passed. All 153 previous generated files/inventories are unchanged; the new example adds
exactly four artifacts. All twenty-three fake-only dependency graphs exclude EF, Models, Designers,
Design, Generation, Tool, Validation and relational providers. Markdown links and tracked whitespace
checks passed.

The complete Release/no-build/no-restore suite passed **2,176 tests** with concurrency bounded to
two threads: 1,759 Tool, 96 EF integration, 309 Testing runtime, 10 attribute-generator and 2 attribute
tests. This slice adds 76 cases (43 Tool, 11 EF and 22 Testing/input-runtime). Twenty-four opt-in
SQL Server cases were skipped because no connection is configured; no SQL Server execution is claimed.
The final local-link audit has zero broken targets and the 56-file feature whitespace audit passed.

## Directional nullable many-to-many reads (2026-10-11)

Delivered the optional F15.3/IM06-IM07 nullable-link extension, including saved-result read reuse:

- Explicit CLR join stores can have optional source FKs: null/incomplete/unmatched source keys
  belong to no source row. Direction is resolved separately for an accessed inverse edge.
- Optional targets require every navigation use to start with a built-in reference-null filter
  (`Where(target => target != null)`, its reversed comparison, or a leading short-circuit guard).
  Subsequent projections/counts/membership, replacement From roots, pages, streams and saved DTOs
  reuse that shared expression. One guarded occurrence cannot authorize an unguarded DAG occurrence.
- SQLite regression cases establish the retained-null projection/count distinction: skipping every
  missing target would silently change an unguarded query. Those operations and explicit target
  Include/ThenInclude/load profiles retain RGD3302/RGD3402/typed-override coverage instead of a SQL
  null-propagation simulator. Inferred guarded projection paths do not invent an unguarded use.
- Both entity-shaped and explicit public-data stores capture endpoints/join rows together, retain
  complete composite/alternate keys and validate the other required FK, complete pair uniqueness,
  key arity and cancellation. Detached results, cold stream snapshots, scripts and baseline reset
  remain intact. No related visibility, FK constraint or graph mutation is inferred.
- Optional edges add directional `optionalSource`/`optionalTarget`/`nullPropagation` receipts;
  required edges and production/contracts/cache fingerprints are unchanged. There is no additional
  generation flag or service-side query surface: fixture stores/reads/edges remain optional.
- [MemoryOptionalLinks](../../samples/ConfigurationExamples/MemoryOptionalLinks/README.md) pairs
  [LinkedNotesRepositoryDesigner](../../samples/ConfigurationExamples/MemoryOptionalLinks/Configuration/LinkedNotesRepositoryDesigner.cs)
  with exactly four outputs. Its internal/read-only model uses public scalar rows; the EF/model/
  designer/generator-free consumer covers guarded targets, inverse counts, visibility, nullable
  links, explicit provider overrides and reset. The index now has forty-one applications,
  twenty-six testing examples and twenty-four fake-only consumers.

See [nullable-link semantics and boundaries](../implemented/testing/INMEMORY-OPTIONAL-LINKS.md). Shared SQLite cases compare
the actual generated named reads and single/batch saved DTOs with both supported fixture shapes;
they do not claim universal SQL/provider null, transaction or concurrency equivalence.

Verification: the complete Release solution build passed with zero warnings/errors. All forty-one
examples regenerated, independently compiled against their actual .NET 8 inputs and ran successfully.
Final generation-freshness and approved Ledger API baseline checks passed. All 157 earlier generated
files/inventories remain byte-identical; the new example adds exactly four artifacts. All twenty-four
fake-only dependency graphs exclude EF, Models, Designers, Design, Generation, Tool, Validation and
relational providers. The 102-document local-link audit and 49-file feature/tracked whitespace checks passed.

The complete Release/no-build/no-restore suite passed **2,229 tests** with concurrency bounded to
two threads: 1,805 Tool, 96 EF integration, 316 Testing runtime, 10 attribute-generator and 2 attribute
tests. The net increase is 53 cases (46 Tool and 7 Testing runtime); the former blanket optional-join
rejection case is superseded by the directional/guarded coverage. Twenty-four opt-in SQL Server cases
were skipped because no connection is configured; no SQL Server execution is claimed.

## Explicit join-row reconciliation (2026-10-12)

Delivered the optional F09/F15.4/IM08-IM09 association-write slice:

- `HasCommand(...).ReconcileLinks(...)` selects an ordinary owner-to-join collection. The final
  model must prove exactly two required disjoint many-to-one FKs, an ordinary keyed leaf join and
  unique complete owner/target pairing. Direct skip-navigation/implicit graph mutation is excluded.
- Matching selects the complete target FK in mapped order, including scalar/composite/alternate
  keys. Add explicitly maps its reference and payload; owner FKs are supplied from the selected
  parent. Both FK roles remain immutable on update. Ordinary ReconcileCollection keeps its old
  FK prohibition; no blanket AllowWrite or business/authorization rule is inferred.
- Existing bounds, missing Keep/Reject/Delete, omitted-versus-empty inputs, parent/link tokens,
  protected payload fields, Stage/shared Save and fresh ReturnsSaved DTOs are reused. Join scope
  retains global/designer link filters, while target visibility is not injected implicitly.
  Neither endpoint is created/updated/deleted by these mappings; shared saves still include earlier
  unrelated pending edits. Database constraints remain authoritative.
- Saving uses the bounded relational transaction/savepoint guard. Link creation rejects populated
  endpoint graphs. Failure restoration preserves earlier pending edits and fixes reverted generated
  join keys before detachment; SQLite cases prove retry safety with automatic detection enabled
  and disabled, loaded inverse/skip navigation fixup and an earlier successful INSERT rolled back.
- Per-command SimulateCollectionWrites reuses the fixture child engine on entity/public scalar
  stores, complete reference keys, payload/version writes, shared pending/saved state, scripts and
  checkpoint/baseline rollback. It does not simulate SQL FK/index/identity/race guarantees.
- RGD3901 and per-collection linkReconciliation receipts describe FK roles, existing-reference-only
  mutation and policy/constraint boundaries. Existing output stays unchanged without declarations.
- [MemoryLinks](../../samples/ConfigurationExamples/MemoryLinks/README.md) pairs
  [BoardLabelsRepositoryDesigner](../../samples/ConfigurationExamples/MemoryLinks/Configuration/BoardLabelsRepositoryDesigner.cs)
  with exactly four outputs. Its internal/read-only model uses explicit public scalar stores;
  the EF/model/designer/generator-free consumer covers linking, payload updates, unlinking, unchanged
  labels/other owners, saved DTOs, Stage/shared Save and rollback. The index now has forty-two
  applications, twenty-seven testing examples and twenty-five fake-only consumers.

See [link semantics and boundaries](../implemented/writes/LINK-RECONCILIATION.md). Ordinary join-only models do not need
a skip-navigation declaration merely to write association rows. Optional/overlapping/third-FK,
implicit/shared, owned/inherited and nested/cascading relationships retain explicit adapter boundaries.

Verification for this slice:

- Release solution build: zero warnings/errors. All forty-two configuration examples regenerated,
  compiled against actual target references and ran successfully; the subsequent freshness check passed.
- Full solution: **2,275 passed**, zero failures (1,851 tool/generation/provider, 96 EF integration,
  316 Testing runtime, 10 attribute-generator and 2 attribute tests). Twenty-four opt-in SQL Server
  cases were skipped because no connection is configured; no SQL Server execution is claimed.
- Forty-six new link cases cover all layouts and entity/public fixture shapes, complete FK/permission
  diagnostics, pre-acquisition guards, SQLite parity, versions, hidden rows, shared saves, failure
  restoration, generated-key retry safety and populated-graph rejection. Together with existing
  collection regressions, the focused run passed all eighty-seven cases.
- All 161 previous generated artifacts are byte-identical; the new example adds four (165 total).
  All twenty-five fake-only dependency inventories exclude EF/models/designers/generation tooling.
  All 104 Markdown documents and 1,674 local links resolve; whitespace checks passed.

## Designer-declared fixture expression translations (2026-10-12)

Delivered the optional F15.3/IM06-IM07/IM15 scalar-call translation slice:

- Model `HasInMemoryTranslation(name, call, replacement)` accepts one to four typed scalar inputs
  and the same public scalar result. Matching uses exact closed method metadata and fixed literal/
  static-member slots, never string replacement, source invocation or captured getter evaluation.
  Unique names/overloads compose across modules; nested calls and acyclic mappings expand safely.
- Only the separate fixture expression view changes. Production SQL/call expressions, namespaces,
  API shapes, cache fingerprints and dependency analysis retain their original definitions.
  Unused registrations and disabled automatic reads leave existing output unchanged.
- Active named-read predicates, visibility/default/explicit sorts, projections, group/result stages,
  membership keys, scalar/keyed terminals, cursor rules/guards, request/context binding and supported
  typed From composition reuse the declarations. Entity-shaped and mapped public stores retain
  member/relationship/model-policy coverage checks, cold streams and script precedence.
- RGD4001-RGD4003 reject ambiguous/malformed call shapes, captures/clocks/nonportable/graph/dropped-input
  replacements and cycles. Unmatched overloads/literals/generic calls still require typed overrides.
  Operation-specific expressionTranslations receipts describe used names and explicit non-provider parity.
- Optional EF-free InMemoryStringPatterns.Like implements explicit ordinal/ordinal-ignore-case literal
  units, percent/underscore wildcards, nulls and a char escape without regex/backtracking. UTF-16 units,
  literal brackets and comparison policies are deliberate, not inferred database collation semantics.
- [MemoryTranslations](../../samples/ConfigurationExamples/MemoryTranslations/README.md) pairs
  [PatternCatalogRepositoryDesigner](../../samples/ConfigurationExamples/MemoryTranslations/Configuration/PatternCatalogRepositoryDesigner.cs)
  with four exact outputs. Its internal model/public immutable seeds and application port show raw/
  escaped/nullable patterns, a registered SQL length function, group projections and streams without
  EF/model/designer/generator dependencies in the consumer. The index now has forty-three applications,
  twenty-eight testing examples and twenty-six fake-only consumers.

See [translation configuration and boundaries](../implemented/testing/INMEMORY-TRANSLATIONS.md). This slice translates named
reads, not write/store/value adapters or saved command results. SQL/provider guarantees and arbitrary
entity/value-object behavior remain explicit adapters or real-provider verification.

Verification for this slice:

- Release solution build: zero warnings/errors. All forty-three examples regenerated, independently
  compiled against actual target references and ran; the final freshness/API-baseline check passed.
- Full solution: **2,341 passed**, zero failures (1,885 tool/generation/provider, 96 EF integration,
  348 Testing runtime, 10 attribute-generator and 2 attribute tests). Twenty-four opt-in SQL Server
  cases were skipped because no connection is configured; no SQL Server execution is claimed.
- Thirty-four new tooling cases cover both fixture shapes/all layouts, production/API/cache isolation,
  exact generic/literal matching, captures/clocks/graphs/ref-input/cycle guards, nested expansion,
  read clauses, paging/cursors/streams/scripts and eight shared actual-generated SQLite contract cases.
  Thirty-two new pattern cases cover null/empty inputs, wildcards, escapes, ordinal comparison choices,
  UTF-16 units, invalid patterns/cultures and non-backtracking execution.
- All 165 previous generated artifacts are byte-identical; the example adds four (169 total).
  All twenty-six fake-only dependency inventories exclude EF/models/designers/generation tooling.
  All 106 Markdown documents and 1,705 local links resolve; whitespace checks passed.

## Per-command fixture expression translations (2026-10-12)

Delivered the optional F15.4/IM08-IM10/IM15 command extension to registered scalar-call translations:

- Command UseInMemoryTranslations(bool) is off by default and independent of named reads.
  It requires the automatic command backend and applicable model registrations; unused registrations
  and disabled command backends add no command receipts or fixture rewrites.
- Supported command selection/active visibility, request guards/context keys, scalar create/update
  values and conditions, versions, bounded create/patch/delete inputs/item keys, child/link scalar
  payloads/visibility, Returns and fresh ReturnsSaved projections reuse the exact typed declarations.
  The immediate/collection simulation opt-ins and declared relationship-read prerequisites remain separate.
- A fixture-only rendering view keeps production SQL/calls, contracts, cache fingerprints and provider
  mappings unchanged. Store/copy/read-map/write-map/generated-value/comparer adapters are not rewritten.
  Immutable keys/components/owner FKs, protected writes, explicit versions, input limits, complete maps
  and model/provider coverage checks cannot be disabled by declaring a scalar translation.
- Pending versus saved rows, shared Stage/Save, batch preparation, original-row immediate setters,
  conditional insert/upsert branches, version failures, checkpoint rollback and committed-result
  failure semantics remain unchanged. Scripts still take precedence; unsupported calls fail on empty stores.
- [MemoryWriteTranslations](../../samples/ConfigurationExamples/MemoryWriteTranslations/README.md) pairs
  [PatternNotesRepositoryDesigner](../../samples/ConfigurationExamples/MemoryWriteTranslations/Configuration/PatternNotesRepositoryDesigner.cs)
  with four actual artifacts. It separates a real CLR normalization helper from a SQL-only length
  projection, showing public mapped seeds, application CQRS ports, Stage/Save/versions, fresh results
  and immediate-delete rollback. The index now has forty-four apps, twenty-nine testing examples and
  twenty-seven fake-only consumers.

See [command configuration, coverage and limits](../implemented/testing/INMEMORY-COMMAND-TRANSLATIONS.md).
These are explicit fixture semantics, not new production behavior or inferred database equivalence.

Verification for this slice:

- Release solution build: zero warnings/errors. All forty-four examples regenerated, independently
  compiled against actual target references and ran; the final strict compiler/freshness/API-baseline
  check passed with input-only bootstrap references.
- Full solution: **2,387 passed**, zero failures (1,931 tool/generation/provider, 96 EF integration,
  348 Testing runtime, 10 attribute-generator and 2 attribute tests). Twenty-four opt-in SQL Server
  cases were skipped because no connection is configured; no SQL Server execution is claimed.
- All **46 new tooling cases** pass: every output layout/entity-public store shape, production/API/cache
  isolation, independent switches, guarded matching/values/results, keyed contexts, bounded batches,
  ordinary/composite-link reconciliation, saved navigation DTOs, immediate/insert/upsert semantics,
  permission/structural/override/cycle guards, precise active-only create receipts and cancellation.
  Eight shared cases exercise actual generated SQLite and fixture implementations.
- All 169 previous generated artifacts are byte-identical; the example adds four (173 total).
  All twenty-seven fake-only dependency inventories exclude EF/models/designers/generation tooling.
  All 108 Markdown documents and 1,741 local links resolve; whitespace checks passed.

## Explicit tracked fixture scalar equality (2026-10-12)

Delivered the bounded F15.4/IM08-IM10/IM15 ordinary value-comparison slice:

- Store-scoped HasInMemoryValueEquality(property, expression) / WithoutInMemoryValueEquality are
  optional and require automatic commands. No policies are inferred or enabled by simple defaults;
  disabled commands emit no helper/receipt. Replacing a store removes its associated declarations.
- Typed self-contained helpers acknowledge compatible CLR scalar-comparer metadata for tracked
  change/version checks, original-token save checks, and single/batch/child/link writes. Production
  source/API/cache definitions, query predicates and store/key/relationship comparison remain unchanged.
  Captures/clocks/helpers/graphs/dropped inputs and unsupported identity/generated/binary-token shapes
  fail with RGD4101/RGD4102; converters/provider/unknown model behavior still needs scripts.
- Non-forced equivalent changes preserve the saved representation. Mixed assignments merge only
  genuinely changed configured fields, including cloned/reused pending entries and batches/children.
  Expected-version writes still force approved assignments. Shared-save preparation failures retain
  prior pending work and cannot partially commit another store.
- Mapped scalar copies and write-map round trips deliberately retain ordinary structural checks:
  a case-insensitive state policy cannot disguise a case-losing writer/copier. Internal persistence
  entities are not instantiated, and arbitrary value-object behavior is not adapted.
- [MemoryScalarEquality](../../samples/ConfigurationExamples/MemoryScalarEquality/README.md) pairs
  [ComparisonNotesRepositoryDesigner](../../samples/ConfigurationExamples/MemoryScalarEquality/Configuration/ComparisonNotesRepositoryDesigner.cs)
  with four actual artifacts. It separates CLR tracking equality, explicit nullable fixture policies,
  literal query matching and provider token collation, while exposing only public seeds/application
  ports. The index now has forty-five apps, thirty testing examples and twenty-eight fake-only consumers.

See [configuration, affected behaviors and conservative boundaries](../implemented/testing/INMEMORY-VALUE-EQUALITY.md).
Focused generation/execution cases verify all layouts/store shapes, disabled/removed settings,
production/API/cache isolation, non-forced/forced/mixed/reused pending writes, token conflicts,
bounded batches/child writes, nullable values, scripts, cancellation, strict round trips and six
shared actual-generated SQLite contract cases.

Verification:

- Release solution build completed with zero warnings/errors. All forty-five examples regenerated,
  strictly compiled against target references, built and ran; the final compilation/freshness check passed.
- Full solution regression: **2,438 passed**, including 1,976 tooling, 354 testing-runtime, 96 EF,
  ten attribute-generator and two attribute cases. All **45 new tooling cases** and **six new runtime
  cases** pass. The 24 SQL Server cases were skipped because no connection is configured;
  no SQL Server execution is claimed.
- All 173 previous generated artifacts are byte-identical; the focused example adds four (177 total).
  All twenty-eight fake-only dependency inventories exclude EF/models/designers/generation tooling.
- All 110 Markdown documents and 1,773 local links resolve; tracked and new-feature whitespace checks passed.

## Explicit shared owner/reference FK columns (2026-10-13)

Delivered the bounded F09/F15.4/IM08-IM09 association-write extension:

- Link-builder AllowSharedReferenceColumns(bool) is independently off by default. It permits
  compatible overlap between the selected owner FK and one required target FK, not a third
  relationship, optional join, arbitrary aggregate or implicit authorization/tenancy policy.
  Enabling it on disjoint links emits nothing; disabling it restores the original boundary.
- Matching retains the complete immutable target FK in metadata order. Typed guards validate
  every supplied shared component against the selected owner's actual principal property before
  that collection is loaded or mutated. Cross-owner keys fail rather than normalize, disappear
  or transfer ownership. Multiple non-adjacent shared components, differently named/reordered
  owner keys and complete alternate target keys are supported.
- Adds must preserve the complete supplied reference key before owner binding. Non-shared owner
  mapping remains forbidden; neither owner/target/matching keys nor endpoint graphs can be updated.
  Production owner-scoped queries, actual FKs, versions, visible mutation scope, shared Save,
  failure restoration/fixup and cache tags retain existing contracts.
- Optional fixtures reuse the same typed guards on entity/public mapped stores. Existing
  SimulateCollectionWrites and backend switches remain required; provider constraints/races are
  not inferred. Pending/shared-save/checkpoint/baseline and saved-result behavior remain separate
  from EF/model construction in the consumer.
- Overlapping link receipts add precise shared-column/owner-property/key-position, validation,
  default CLR comparison and no-normalization/transfer boundaries. RGD3902 covers missing
  permission/incompatible sharing; existing matching/write/coverage diagnostics remain active.
- [MemorySharedLinks](../../samples/ConfigurationExamples/MemorySharedLinks/README.md) pairs
  [ScopedResourcesRepositoryDesigner](../../samples/ConfigurationExamples/MemorySharedLinks/Configuration/ScopedResourcesRepositoryDesigner.cs)
  with four actual artifacts and handwritten CQRS ports. Its EF-free consumer rejects cross-partition
  references, merges/stages/saves links, reads existing resources, preserves another owner and
  exercises rollback/baseline reset. The index has forty-six apps, thirty-one testing examples
  and twenty-nine fake-only consumers.

See [configuration, comparison/ownership semantics and limits](../implemented/writes/SHARED-REFERENCE-COLUMNS.md).
All forty-four focused tooling cases and seven provider-neutral runtime cases pass. Eleven actual
generated SQLite cases cover both fixture shapes, complete shared keys, versions, shared saving,
rollback, pretracked endpoint fixup, failed-save restoration with automatic detection enabled/disabled,
and multiple non-adjacent shared components with alternate target keys.

Verification:

- Release solution build passed with zero warnings/errors. All forty-six examples regenerated,
  strictly compiled against actual .NET 8 references, built and ran. The final no-write freshness,
  strict compilation and approved Ledger API-baseline checks passed.
- Full solution: **2,489 passed**, zero failures: 2,020 tooling, 361 Testing runtime, 96 EF integration,
  ten attribute-generator and two attribute cases. All **44 new tooling cases** and **seven new
  runtime cases** pass, including common key/owner input-name hygiene.
  Twenty-four SQL Server tests were skipped because no connection is configured; no SQL Server
  execution is claimed.
- All 177 previous generated artifacts are byte-identical; this focused example adds four
  (181 total). All twenty-nine fake-only dependency inventories exclude EF/models/designers/generation tooling.
- All 112 Markdown documents and 1,808 local links resolve. Tracked and new-feature whitespace
  checks passed; normal shared contract binaries were restored after input-only validation.

## Named target and additional link-reference roles (2026-10-13)

Delivered the bounded F09/F15.4/IM08-IM09 multi-reference association-write extension:

- TargetReference names the complete matching-target FK; AdditionalReference declares each
  remaining required dependent reference. All FK roles must be accounted for exactly once.
  No global switch or target inference among several relationships is added; unconfigured
  two-FK output remains unchanged. WithoutTargetReference/WithoutAdditionalReference remove settings.
- Add must initialize every scalar component of every additional reference, including composite
  alternate keys. Constructor/database defaults and reverse DTO mappings are not inferred.
  Additional permission is creation-only and does not bypass ProtectWrite/AllowWrite policies.
- Matched rows retain all FK values even when Add-only request values differ. Updates still
  cannot change owner/target/additional/primary/matching keys or referenced graphs. Only approved
  payload/version setters execute; referenced entities are not created, updated or deleted.
- Additional references are required, non-unique, non-ownership and disjoint from all other roles.
  Existing owner/target shared-column permission remains separately explicit. Optional/overlapping
  additional/owned/inherited/shared-property-bag/nested graph shapes remain extensions/adapters.
- Production and independently opted-in fixtures reuse bounded owner-scoped reconciliation,
  versions, visibility, shared Stage/Save, saved DTOs and rollback. Database FKs and authorization
  are not simulated or inferred by fixtures. Pretracked fixup and prior unrelated pending work
  survive FK/check-constraint save failures.
- Receipts identify the target navigation, every additional dependent/principal key and
  complete initialization/immutable-match/database-only-existence boundaries.
  RGD4201/RGD4202 diagnose roles and missing components; prior protection/coverage checks still apply.
- [MemoryAdditionalReferences](../../samples/ConfigurationExamples/MemoryAdditionalReferences/README.md)
  pairs [ReferenceCatalogRepositoryDesigner](../../samples/ConfigurationExamples/MemoryAdditionalReferences/Configuration/ReferenceCatalogRepositoryDesigner.cs)
  with four actual outputs. Five public stores and handwritten CQRS ports demonstrate four FK
  roles, composite/scalar initialization, immutable matches, fresh references and Stage/Save/rollback
  without EF/model/designer/generator consumer dependencies. The index has forty-seven apps,
  thirty-two testing examples and thirty fake-only consumers.

See [configuration, semantics and limits](../implemented/writes/ADDITIONAL-LINK-REFERENCES.md).
All 36 focused tooling cases pass, including eight actual generated SQLite cases for both
fixture shapes, complete reference mapping, immutable matched values, shared saves/rollback and
additional FK/check-constraint failures with automatic detection enabled/disabled.

Verification:

- Full Release solution build: zero warnings/errors. Complete bounded-parallelism regression:
  **2,525 passed** (2,056 Tool, 361 Testing, 96 EF runtime, 10 attribute generator, 2 attributes),
  including the 36 new tooling cases. The 24 opt-in live SQL Server cases remain skipped because
  `REPOGEN_SQLSERVER_TEST_CONNECTION` is not configured.
- All forty-seven designer apps were strictly generated, compiled against their target input-only
  references, built and executed. Final no-write freshness/compilation and approved API baseline
  checks passed.
- All 181 previous generated artifacts are byte-identical; this example adds four (185 total).
  All thirty fake-only dependency inventories exclude EF/models/designers/generation tooling.
- All 114 Markdown documents and 1,843 local links resolve. Tracked and new-feature whitespace
  checks passed; normal shared contract binaries were restored after input-only validation.

## Optional additional link-reference roles (2026-10-13)

Delivered the bounded F09/F15.4/IM08-IM09 nullable-reference write extension:

- OptionalAdditionalReference explicitly names an optional extra FK. Required AdditionalReference
  declarations remain distinct; both share duplicate/removal rules and can coexist on one command.
  No global setting, inferred role, implicit fixture/navigation option or new default dependency is added.
- Every component must be nullable in the model and CLR, non-generated and disjoint from other
  roles. Matching target and owner remain required. Mixed-nullability/generated/overlapping extra
  keys and optional matching targets retain explicit diagnostics/adapter boundaries.
- Add must initialize every nullable FK component. All-null means absent; all-present means a
  complete reference. Partial-null composite keys are rejected by typed generated checks, without
  reflection, normalization or inferred database/constructor values.
- The approved initializer runs once per new link. Add-only inputs on matched links are neither
  validated nor applied: values and presence/absence remain immutable. Only approved payload/version
  setters run; neither endpoint graphs nor referenced rows are persisted.
  Optional composite fixture creation prepares all new initializers before matched-row version checks,
  matching production failure precedence without reserving generated IDs early or affecting defaults.
- Production and public/entity-shaped fixtures reuse existing bounded reconciliation, visibility,
  versions, shared Stage/Save, saved DTOs and rollback. Partial-key failures retain earlier unrelated
  pending work; database FK/check failures restore nullable relationship fixup with change detection
  enabled/disabled. Fixtures deliberately do not enforce SQL FKs or infer related authorization.
- Optional receipts add per-role cardinality, all-or-none null semantics and creation-validation
  timing. RGD4301 diagnoses unrepresentable/generated absence; existing role, mapping, immutable-key,
  property-protection and fixture-coverage diagnostics remain in force. Required/unconfigured output
  remains byte-identical.
- [MemoryOptionalReferences](../../samples/ConfigurationExamples/MemoryOptionalReferences/README.md)
  pairs [OptionalAssignmentsRepositoryDesigner](../../samples/ConfigurationExamples/MemoryOptionalReferences/Configuration/OptionalAssignmentsRepositoryDesigner.cs)
  with four actual outputs. Four public stores, handwritten CQRS ports and internal persistence
  types demonstrate absence/presence, immutable matches, partial-key rejection and Stage/Save/rollback
  without EF/model/designer/generator consumer dependencies. The index has forty-eight apps,
  thirty-three testing examples and thirty-one fake-only consumers.

See [configuration, semantics and conservative boundaries](../implemented/writes/OPTIONAL-ADDITIONAL-REFERENCES.md).
All 47 focused tooling cases pass, including 14 actual generated SQLite cases for public/entity-shaped
fixtures, absent/present references, immutable matches, partial-key rejection, shared saves/rollback,
FK/check failures with change detection enabled/disabled and combined stale-version/partial-key precedence.

Verification:

- Full Release solution build: zero warnings/errors. Complete bounded-parallelism regression:
  **2,572 passed** (2,103 Tool, 361 Testing, 96 EF runtime, 10 attribute generator, 2 attributes),
  including the 47 new tooling cases. The 24 opt-in live SQL Server cases remain skipped because
  `REPOGEN_SQLSERVER_TEST_CONNECTION` is not configured.
- All forty-eight designer apps were strictly generated, compiled against target input-only
  references, built and executed. Final no-write freshness/compilation and approved API baseline
  checks passed.
- All 185 previous generated artifacts are byte-identical; this example adds four (189 total).
  All thirty-one fake-only dependency inventories exclude EF/models/designers/generation tooling.
- All 116 Markdown documents and 1,879 local links resolve. Tracked and all 48 new-feature file
  whitespace checks passed; normal shared contract binaries were restored after input-only validation.

## Explicit null-preserving fixture navigation reads (2026-10-13)

Delivered the bounded F15.3/IM06-IM07 nullable-target reporting extension:

- HasInMemoryNavigation(navigation, preserveMissingTargets: true) independently enables one null
  slot per missing optional-target join row. Defaults remain guarded-only; ordinary/required-target
  navigations reject this policy with RGD4401. Source and inverse declarations remain directional.
- Null/incomplete/unmatched optional targets retain slot counts; missing optional sources still
  belong to no owner. Complete duplicate pairs, principal key uniqueness, required references,
  key arity/shape and cancellation remain enforced before edge assignment.
- A dedicated occurrence-based inspector proves built-in null tests, short-circuit/conditional
  branch safety and bounded scalar/DTO pipelines without executing expressions. Unsafe dereferences,
  indexed/custom-comparer/overloaded-equality shapes, entity/object identity escape, raw collections
  and explicit optional-target Include/load profiles retain strict diagnostics/typed overrides.
- Counts, missing-target presence, conditional ordering/projections, named projection reuse,
  typed From, pages/cold streams and fresh single/batch saved results use the same declared expressions.
  Production queries/contracts/cache shapes are unchanged; SQL null propagation is not rewritten.
- Entity-shaped and public scalar-mapped fixtures retain coordinated detached source/target/join
  snapshots, join order, root-versus-related visibility and baseline isolation. Only explicitly
  accessed edges load; no related policy, graph write or SQL constraint simulation is inferred.
- Opted-in manifests record preserved missing-target slots and explicit CLR null-safety rather
  than general SQL propagation/loading. Required, unconfigured and prior guarded-only output remains
  byte-identical.
- [MemoryNullTargets](../../samples/ConfigurationExamples/MemoryNullTargets/README.md) pairs
  [NullTargetNotebookRepositoryDesigner](../../samples/ConfigurationExamples/MemoryNullTargets/Configuration/NullTargetNotebookRepositoryDesigner.cs)
  with four outputs. Three public stores, internal read-only models and a handwritten read port
  demonstrate raw/missing counts, reusable nullable DTOs, page/stream snapshots and baseline reset
  without EF/model/designer/generator consumer dependencies. The index has forty-nine apps,
  thirty-four testing examples and thirty-two fake-only consumers.

See [null-safety semantics and conservative boundaries](../implemented/testing/INMEMORY-NULL-TARGETS.md).
Verification:

- All 60 focused tooling cases and 10 runtime cases passed. Four generated SQLite contract cases
  compare public/entity-shaped stores and both nullable FK directions across counts, presence,
  explicit conditional DTOs, named/From projections, paging and saved single/batch results.
- Full Release solution build: zero warnings/errors. Complete bounded-parallelism regression:
  **2,642 passed** (2,163 Tool, 371 Testing, 96 EF runtime, 10 attribute generator, 2 attributes),
  including the 70 new cases. The 24 opt-in live SQL Server cases remain skipped because
  `REPOGEN_SQLSERVER_TEST_CONNECTION` is not configured.
- All forty-nine designer apps were strictly generated, compiled against target input-only
  references, built and executed. Final no-write freshness/compilation and approved API baseline
  checks passed.
- All 189 previous generated artifacts are byte-identical; this example adds four (193 total).
  All thirty-two fake-only dependency inventories exclude EF/models/designers/generation tooling.
- All 118 Markdown documents and 1,912 local links resolve. Tracked and all 27 new-feature file
  whitespace checks passed; normal shared contract binaries were restored after input-only validation.

## Explicit fixture relationship-key comparison (2026-10-13)

Delivered the bounded F15.3/IM07 relationship-comparison extension:

- HasInMemoryRelationshipComparer(principalKeyProperty, comparerFactory) independently selects
  one non-null primary/alternate-key scalar. Structural comparison remains the default for all
  other components. Removing the declaration or replacing/removing its store resets it.
- Both reference/collection directions and explicit CLR many-to-many source/target join keys
  use the declared principal policy. Uniqueness and complete-pair validation use the same comparison;
  null/incomplete FKs still never join, including with missing-target preservation enabled.
- Factories are inspected/emitted without invoking constructors/getters or EF comparers. Typed
  equality/hashing requires no reflection-based member lookup. RGD4501 rejects missing stores,
  duplicate/non-principal/nullable/array/type-mismatched components and captured/ambient factories.
- Compatible explicit CLR comparer metadata can be acknowledged for principal/FK components.
  Converters, provider comparers, inconsistent metadata, shadow keys, model filters/AutoInclude and
  owned/shared/inherited relationships retain their existing override/provider boundaries.
- Coordinated detached snapshots, root-versus-related visibility, pages/cold streams and fresh
  saved single/batch DTOs reuse the policy. Query predicates, store identity, tracked-write comparison,
  matching-key permissions, copy/write-map round trips and production/API/cache shapes are unchanged.
- Opted-in receipts describe relationship/uniqueness scope, complete-key/no-null-match rules and
  no inferred provider equivalence. Existing unconfigured generated files remain byte-identical.
- [MemoryRelationshipComparers](../../samples/ConfigurationExamples/MemoryRelationshipComparers/README.md)
  pairs [ComparedDirectoryRepositoryDesigner](../../samples/ConfigurationExamples/MemoryRelationshipComparers/Configuration/ComparedDirectoryRepositoryDesigner.cs)
  with four outputs. Two public stores, internal read-only models and handwritten ports demonstrate
  composite scope, both directions, unchanged predicates, null FKs, detached streams, uniqueness
  across distinct primary IDs and baseline reset, without EF/model/designer/tool consumer dependencies.
  The index has fifty apps, thirty-five testing examples and thirty-three fake-only consumers.

See [comparison scope and provider boundaries](../implemented/testing/INMEMORY-RELATIONSHIP-COMPARERS.md).

Verification:

- All 43 focused tooling cases and 16 new runtime cases passed. Six actual generated SQLite
  contract cases compare both store shapes, composite references/collections, both many-to-many
  directions, nullable targets, pages and fresh saved single/batch DTOs. All output layouts also
  compile and execute independently compared source/target key components.
- Full Release solution build: zero warnings/errors. Complete bounded-parallelism regression:
  **2,701 passed** (2,206 Tool, 387 Testing, 96 EF runtime, 10 attribute generator, 2 attributes),
  including all 59 new cases. The 24 opt-in live SQL Server cases remain skipped because
  `REPOGEN_SQLSERVER_TEST_CONNECTION` is not configured.
- All fifty designer apps were strictly generated, compiled against target input-only references,
  built and executed. Final no-write freshness/compilation and approved API baseline checks passed;
  normal shared contract binaries were restored after input-only validation.
- All 193 previous generated artifacts are byte-identical; the new example adds four (197 total).
  All thirty-three fake-only dependency inventories exclude EF/models/designers/generation tooling.
- All 120 Markdown documents and 1,949 local links resolve. Tracked and feature-file whitespace
  checks passed.

## Explicit flat DTO helper recipes (2026-10-13)

Delivered the bounded F15.3/IM06-IM07 projection-helper extension:

- HasInMemoryProjectionTranslation accepts one-to-four scalar inputs and explicitly constructs
  an independent public flat DTO result. Records/structs, scalar initializers, conditional null
  results, registered scalar calls and acyclic projection aliases share exact overload/fixed-slot
  matching. Existing scalar registrations retain their original strict construction boundary.
- Final-model validation excludes persistence/derived/generic entity-bearing results; captured
  state, dropped arguments, clocks, graph/service/private/interface/abstract results, private
  constructors and non-instance/non-scalar bindings are rejected without executing expressions,
  constructors, getters or source helpers. RGD4601/4602 identify the new DTO/type/body boundary.
- Named/named-projection reads, From/group shapes, counted pages/cold streams and separately
  opted-in saved single/batch results reuse declared semantics. Visibility, version outcomes,
  detached snapshots, Stage/shared Save, input ordering, cancellation and overrides are unchanged.
- Production keeps its original mapper/API/cache expressions. Store/copy/scalar assignments and
  adapters are not rewritten. Real SQLite cases verify top-level DTO mapping; a deliberate opaque
  helper predicate still fails SQL translation, with no new client filter fallback.
- Used receipts add ExplicitFlatDtoConstruction; unused/disabled registrations and all prior
  scalar receipts remain unchanged. Arbitrary DTO constructor behavior and provider equivalence
  are not inferred or proved by expression validation.
- [MemoryProjectionHelpers](../../samples/ConfigurationExamples/MemoryProjectionHelpers/README.md)
  pairs [ProjectionMemosRepositoryDesigner](../../samples/ConfigurationExamples/MemoryProjectionHelpers/Configuration/ProjectionMemosRepositoryDesigner.cs)
  with exact production/contract/fixture/manifest files. An internal model and public immutable
  seeds demonstrate named read/write ports, original versus fixture mapping, unchanged persisted
  text, version conflicts, pages, cold streams and baseline isolation without EF/model/tool dependencies.
  The index has fifty-one apps, thirty-six testing examples and thirty-four fake-only consumers.

See [flat DTO semantics and SQL boundaries](../implemented/testing/INMEMORY-PROJECTION-TRANSLATIONS.md).

Verification:

- All 39 focused cases passed, covering every output layout and both store shapes, exact/fixed-slot
  matching, scalar/DTO registration separation, initializer/struct/null results, constructor/static
  binding guards, model-aware entity exclusion, stale/missing outcomes, detached/cold reads,
  Stage/shared Save and fresh input-ordered batches. Two generated SQLite contract cases compare
  top-level helper results; a third verifies no production client-filter fallback for an opaque predicate.
- Full Release solution build: zero warnings/errors. Complete bounded-parallelism regression:
  **2,740 passed** (2,245 Tool, 387 Testing, 96 EF runtime, 10 attribute generator, 2 attributes).
  The 24 opt-in live SQL Server cases remain skipped because `REPOGEN_SQLSERVER_TEST_CONNECTION`
  is not configured.
- All fifty-one designer apps were strictly generated, compiled against target input-only references,
  built and executed. Final no-write freshness/compilation and approved API baseline checks passed;
  normal shared contract binaries were restored after input-only validation.
- All 197 previous generated artifacts are byte-identical; the new example adds four (201 total).
  All thirty-four fake-only dependency inventories exclude EF/models/designers/generation tooling.
- All 122 Markdown documents and 1,979 local links resolve. Tracked and 38 feature-relevant file
  whitespace checks passed.

## Optional final-model scalar fixture filters (2026-10-13)

Delivered the bounded F15.3/IM06 model-filter read extension:

- Per-entity UseInMemoryModelQueryFilter opts in to the final EF model's portable Boolean scalar
  filter. Off by default; disabling it, an absent filter or a disabled read backend adds no output.
- Ordinary reads and every separately opted-in From root filter before designer visibility and
  query composition, including joins/correlation/repeated roots/set operations, projections,
  groups/dictionaries/counts/pages and cold detached streams. Seeds/baselines are not mutated.
- IgnoreQueryFilters bypasses all model filters for the operation; BypassVisibility affects only
  the named designer policy. Production repositories, ports, API/cache shapes and EF metadata
  remain unchanged. This setting does not enable filtered writes or saved-result graphs.
- Context/captured values, clocks/static state, whole-row behavior, navigation/shadow/field-only/
  unmapped/inherited shapes and AutoInclude retain explicit override boundaries. Strict read
  coverage uses RGD3302. Scalar helper translations still pass row/member/capture checks; no
  expression, getter, constructor or source helper executes during generation.
- Active filtered navigation graphs remain conservative overrides because EF relationships can
  prune roots; globally ignored filters retain the previous declared unfiltered navigation subset.
- Used operation receipts describe final-model roots/predicates, execution order, independent
  bypasses and no provider equivalence. The existing unconfigured examples remain unchanged.
- [MemoryModelFilters](../../samples/ConfigurationExamples/MemoryModelFilters/README.md) pairs
  [FilteredNotebookRepositoryDesigner](../../samples/ConfigurationExamples/MemoryModelFilters/Configuration/FilteredNotebookRepositoryDesigner.cs)
  with exact production/contract/fixture/manifest outputs. Two internal tables and immutable
  public seeds demonstrate independent delete/archive/visibility rules, From joins, counted pages,
  cold streams and baseline isolation without EF/model/designer/tool consumer dependencies.
  The index has fifty-two apps, thirty-seven testing examples and thirty-five fake-only consumers.

See [filter semantics and conservative boundaries](../implemented/testing/INMEMORY-MODEL-FILTERS.md).

Verification:

- All 39 focused cases passed, including every output layout and both store shapes, independent
  bypasses, filtered roots/joins/correlation/set operations/pages/streams, cancellation, mapped
  member coverage, capture/clock/context/identity/shadow/navigation/AutoInclude guards and retained
  write boundaries. Two generated SQLite contract cases compare complete named read pipelines;
  a third verifies no production client-filter fallback for an opaque model-filter helper.
- Complete bounded-parallelism regression: **2,779 passed** (2,284 Tool, 387 Testing, 96 EF runtime,
  10 attribute generator, 2 attributes). The 24 opt-in live SQL Server cases remain skipped because
  `REPOGEN_SQLSERVER_TEST_CONNECTION` is not configured.
- Full Release solution build: zero warnings/errors. All fifty-two designer apps were strictly
  generated, compiled against target input-only references, built and executed. Final no-write
  freshness/compilation and approved API baseline checks passed; normal shared contract binaries
  were restored after input-only validation.
- All 201 previous generated artifacts are byte-identical; the new example adds four (205 total).
  All thirty-five fake-only dependency inventories exclude EF/models/designers/generation tooling.
- All 124 Markdown documents and 2,012 local links resolve. Tracked and all 49 feature-relevant file
  whitespace checks passed.

## Optional per-command scalar model filters (2026-10-13)

Delivered the bounded F15.4/IM08-IM09 model-filter write extension:

- Command UseInMemoryModelQueryFilter opts in independently of entity read settings and of
  UseInMemoryTranslations/SimulateImmediateWrites. Off per command; no filter/disabled backend
  adds no source/receipt. Production repositories, ports, API/cache shapes and metadata stay unchanged.
- Scalar tracked Create/Patch/TrackedDelete, bounded CreateMany/PatchMany/leaf DeleteMany and
  non-returning immediate Update/Delete counts reuse portable model filters for saved selection.
  Model filters apply before designer visibility/predicates and do not disappear with BypassVisibility.
- Inserts are not validated/filtered. Shared Save does not re-check query filters or restrict itself
  to visible rows. Staged archives still select their saved row, use pending values and retain
  original-token checks and approved-column merges. Immediate counts retain pending isolation.
- Fresh scalar/DTO ReturnsSaved reapplies the filter after the shared save. Hidden/missing single
  or batch results fail after saving rather than silently shorten/undo writes. Explicit transaction
  rollback remains available; ordinary Returns retains affected-row mapping instead of a fresh query.
- Context/captured/ambient/identity/shadow/unmapped/AutoInclude policies retain strict RGD3402
  coverage. Complete scalar write maps retain RGD3601 validation. Optional scalar translations
  require the command's separate opt-in and preserve row/member/capture checks without invocation.
- Filtered graph reconciliation/saved navigation graphs, native returning and atomic insert/upsert
  remain scripts/real-provider extensions. Receipts state selection, insertion, Save, fresh-result
  scope and no inferred provider equivalence. Existing unconfigured examples remain unchanged.
- [MemoryModelWrites](../../samples/ConfigurationExamples/MemoryModelWrites/README.md) pairs
  [FilteredDraftsRepositoryDesigner](../../samples/ConfigurationExamples/MemoryModelWrites/Configuration/FilteredDraftsRepositoryDesigner.cs)
  with exact production/contract/fixture/manifest outputs. Two internal tables, immutable public
  rows and handwritten read/write/audit ports demonstrate model filters, versions, saved-row staging,
  cross-repository Save, immediate counts, unfiltered creation and explicit saved-result rollback.
  The index has fifty-three apps, thirty-eight testing examples and thirty-six fake-only consumers.

See [command filter semantics and boundaries](../implemented/testing/INMEMORY-MODEL-WRITE-FILTERS.md).

Verification:

- All 46 focused cases passed across every output layout and both store shapes, separate read/write/
  translation/backend opt-ins, complete mapped state, filtered missing/stale/bypass outcomes,
  batch preparation/input order, saved-row staging/shared Save, immediate pending isolation,
  insertion/fresh-result differences, cancellation, captured/context/shadow/AutoInclude guards
  and graph/native boundaries. Six actual generated SQLite contract cases compare scalar writes,
  batches/creates/counts, staged soft deletion and explicit rollback after post-save result failure.
- Complete bounded-parallelism regression: **2,825 passed** (2,330 Tool, 387 Testing, 96 EF runtime,
  10 attribute generator, 2 attributes). The 24 opt-in live SQL Server cases remain skipped because
  `REPOGEN_SQLSERVER_TEST_CONNECTION` is not configured.
- Full Release solution build: zero warnings/errors. Final no-write freshness/compilation and
  approved API baseline checks passed; normal shared contract binaries were restored after
  input-only validation.
- All fifty-three designer apps were strictly generated, compiled against target input-only
  references, built and executed. All 205 previous generated artifacts are byte-identical;
  this example adds four (209 total).
- All thirty-six fake-only dependency inventories exclude EF/models/designers/generation tooling.
  All 126 Markdown documents and 2,049 local links resolve. Tracked and all 52 feature-relevant
  file whitespace checks passed.

## Optional named-query model-loading control (2026-10-13)

Delivered the bounded F08/F15.3/IM07 loading-policy extension:

- Query and current result builders expose IgnoreAutoIncludes(bool). Off by default; false restores
  model loading. It configures the whole named query before projections/grouping/terminals and each
  typed From root, not a service-side runtime DSL or new context acquisition strategy.
- Production sources use EF's policy while retaining explicit Include/ThenInclude, entity/projection
  loading defaults and named profiles. RepoGen AutoIncludeNavigations remains a separate switch.
  Query filters, visibility, tracking, splitting, tags, cache contracts and cancellation stay independent.
- Eligible fixture reads no longer need to simulate disabled user-configured automatic loading.
  Existing scalar/multi-root/declared navigation and required explicit many-to-many subsets remain;
  explicit stores/maps/edges are still required. No EF operator is emitted in test-double source.
- Owned eager loading, inheritance, active unsupported filters, filtered includes and unrelated
  nonportable shapes retain overrides. Read settings do not enable graph writes or change fresh
  saved-command policy coverage. Registered providers reject the setting with RGD1304; superseded
  result builders cannot mutate it. Strict reads retain RGD3302 for unacknowledged model loading.
- Only opted-in queries add ignoreAutoIncludes:true to manifests/metadata. Public ports/API shapes
  are unchanged. All unconfigured sample artifacts remain byte-stable.
- [MemoryLoadingPolicies](../../samples/ConfigurationExamples/MemoryLoadingPolicies/README.md) pairs
  [ExplicitLoadingRepositoryDesigner](../../samples/ConfigurationExamples/MemoryLoadingPolicies/Configuration/ExplicitLoadingRepositoryDesigner.cs)
  with four actual outputs. Internal models/public immutable seeds demonstrate explicit detail
  loading, root visibility, multi-source reads, counted pages, cold streams and detached graph/
  baseline isolation. The index has fifty-four apps, thirty-nine testing examples and thirty-seven
  fake-only consumers.

See [scope, semantics and conservative boundaries](../implemented/queries/EXPLICIT-LOADING-POLICIES.md).

Verification: 36 focused cases passed (33 new, three existing loading guards), including every
output layout/both store shapes, toggling/defaults, API stability, provider ownership, owned/related
filter/filtered-include boundaries, root filter opt-in, explicit skip-navigation reads and retained
command coverage. Five SQLite cases compare generated named/grouped pipelines, compatibility
queries and actual entity-graph materialization with model loading enabled/disabled/explicitly loaded.

- Complete bounded-parallelism regression: **2,858 passed** (2,363 Tool, 387 Testing, 96 EF runtime,
  10 attribute generator, 2 attributes). The 24 opt-in live SQL Server cases remain skipped because
  REPOGEN_SQLSERVER_TEST_CONNECTION is not configured.
- Full Release solution build: zero warnings/errors. All fifty-four designer applications were
  strictly generated, compiled against target input-only references, built and executed. Final
  no-write freshness/compilation and approved API-baseline checks passed; normal shared contract
  binaries were restored after input-only validation.
- All 209 previous generated artifacts are byte-identical; this example adds four (213 total).
  All thirty-seven fake-only dependency inventories exclude EF/models/designers/generation tooling.
- All 128 Markdown documents and 2,085 local links resolve. Tracked and all 48 feature-relevant
  file whitespace checks passed.

## Optional constant model defaults and scalar sentinels (2026-10-14)

Delivered the bounded F15.4/IM04/IM08 insertion-policy extension:

- UseInMemoryModelDefaults opts one preceding writable store into eligible final-model constant
  insert defaults. Off by default; false, replacement and removing write maps disable it. Automatic
  commands/stores stay separate. Production expressions, contracts/API/cache and permissions stay unchanged.
- Explicit factories win and run in declaration order, ahead of remaining model constants in ordinal
  property order. Typed custom scalar sentinels work with explicit factories without this opt-in.
  Zero/false/null/enum values survive when not the sentinel.
- Primitive/enum/string/decimal/Guid/date/time literals require no SQL/application helper evaluation.
  Typed narrow/nullable/enum comparisons avoid boxing mismatches; escaped controls and unnamed enums
  stay safe. No compiler dependency is added to the Generation library.
- Default CLR factory output stays unchanged. SQL/computed/update/key/FK/token/binary/object/provider
  defaults remain explicit adapters/overrides. Custom conversion/comparison sentinels retain RGD3501;
  unsupported values retain RGD3402 and incomplete maps retain structural boundaries.
- Creation maps explicitly assign custom-sentinel fields, without inferred field initializers.
  Shared Stage/Save, bounded preparation/results and rollback remain existing semantics. Seeds,
  baselines and updates do not rerun defaults. Extra receipts state origin/order/sentinels and
  no provider equivalence; no simple-user infrastructure or service-side default DSL is added.
- [MemoryModelDefaults](../../samples/ConfigurationExamples/MemoryModelDefaults/README.md) pairs
  [DefaultSettingsRepositoryDesigner](../../samples/ConfigurationExamples/MemoryModelDefaults/Configuration/DefaultSettingsRepositoryDesigner.cs)
  with four outputs and an EF/model-free consumer. The index has fifty-five apps, forty testing
  examples and thirty-eight fake-only consumers.

See [semantics, precedence and boundaries](../implemented/testing/INMEMORY-MODEL-DEFAULTS.md).

Verification: all 56 new focused cases passed, including all layouts/both store shapes, API stability,
separate opt-ins, typed constant round trips, supplied values, staging/batches/rollback/cancellation,
precedence and SQL/computed/key/conversion/field-initializer guards. Two SQLite cases compare
defaults/supplied values, batches, staged shared saves and insert-only behavior.

- Complete bounded-parallelism regression: **2,914 passed** (2,419 Tool, 387 Testing, 96 EF runtime,
  10 attribute generator, 2 attributes). The 24 live SQL Server cases remain skipped without
  REPOGEN_SQLSERVER_TEST_CONNECTION.
- Full Release solution build: zero warnings/errors. All fifty-five designer apps were generated,
  compiled against target input-only references, built and executed. Final no-write freshness/
  compilation and approved API-baseline checks passed; normal contract binaries were restored.
- All 213 previous generated artifacts are byte-identical; this example adds four (217 total).
  All thirty-eight fake-only dependency inventories exclude EF/models/designers/generation tooling.
- All 130 Markdown documents and 2,115 local links resolve. Tracked and all 51 feature-relevant
  file whitespace checks passed.

## Optional scalar model filters before navigation loading (2026-10-14)

Delivered the bounded F15.3/IM06-IM07 read extension:

- UseInMemoryModelQueryFilter(includeNavigations: true) independently permits one entity's portable
  scalar filter on declared fixture graphs. Off by default; every filtered participant opts in,
  including the root. The parameterless call remains row-only and false clears both permissions.
- One detached participating-store snapshot is mapped and filtered once per dataset before
  relationship assignment, designer root visibility and named pipelines. Collections omit filtered
  children; optional references become null without implicitly deleting their sources.
- Optional reference values require occurrence-local null guards. Guards on another receiver,
  FK presence, earlier stages, raw identity escape and unsafe SQL propagation retain RGD3302.
  Required filtered references in either direction and filtered many-to-many stay scripted.
- IgnoreQueryFilters bypasses every graph/root model filter, independently of designer visibility.
  Related repository visibility is not injected; explicit Include/profile paths stay separate.
  Seeds, baselines, shared snapshots, pages, cold streams and cancellation keep existing semantics.
- Portable related filters retain mapped-member validation and explicit scalar translations.
  Captured contexts, navigation-based filters, AutoInclude and unsupported provider operations
  remain boundaries. Read settings do not enable graph writes or filtered ReturnsSaved.
- Production, contracts/API/cache, permissions and unconfigured output stay unchanged. Graph-only
  receipts state opt-in, predicates, application order and scope without claiming SQL equivalence.
- [MemoryFilteredNavigation](../../samples/ConfigurationExamples/MemoryFilteredNavigation/README.md)
  pairs [FilteredShelfRepositoryDesigner](../../samples/ConfigurationExamples/MemoryFilteredNavigation/Configuration/FilteredShelfRepositoryDesigner.cs)
  with four outputs and a consumer referencing only public contracts/testing/DI. The index has
  fifty-six apps, forty-one testing examples and thirty-nine fake-only consumers.

See [configuration, null safety and boundaries](../implemented/testing/INMEMORY-FILTERED-NAVIGATION.md).

Focused verification: all 41 new cases and 15 existing filter-boundary cases passed, including
all layouts/both store shapes, separate opt-ins, API/output stability, aggregates/references,
From roots, explicit loading, visibility/bypasses, paging, snapshot isolation and null-guard proofs.
Two cases compare actual generated SQLite operations with both fixture store shapes.

- Full Release solution build: zero warnings/errors. All fifty-six designer apps were generated,
  compiled against target input-only references, built and executed.
- All 217 previous generated artifacts are byte-identical; the example adds four (221 total).
  All thirty-nine fake-only dependency inventories exclude EF/models/designers/generation tooling.
- All 132 Markdown documents and 2,146 local links resolve. Tracked and all 49 feature-relevant
  file whitespace checks passed.
- Final no-write freshness/compilation and approved API-baseline checks passed; normal contract
  binaries were restored and the focused consumer passed again.
- Complete bounded-parallelism regression: **2,955 passed** (2,460 Tool, 387 Testing, 96 EF runtime,
  10 attribute generator, 2 attributes). The 24 live SQL Server cases remain skipped without
  REPOGEN_SQLSERVER_TEST_CONNECTION.

## Optional model filters in fresh saved navigation DTOs (2026-10-14)

Delivered the bounded F15.4/IM08 saved-result read extension:

- Command UseInMemoryModelQueryFilter(includeNavigations: true) separately permits filtered
  ordinary ReturnsSaved graphs. Off per command; parameterless retains scalar permission and
  false clears both. The command owns its root dataset's permission independently of entity reads;
  other filtered entity datasets require their existing separate graph permission.
- Shared Save completes before one coordinated detached snapshot, public scalar maps, per-dataset
  filtering, relationship assignment and result visibility/key correlation. Root filters run once,
  not again after loading. Other repositories' staged related-table changes affect fresh DTOs.
- Collections exclude filtered children without injecting unrelated root visibility. Optional
  targets require local null guards; required filtered references, filtered skip graphs, captures,
  active AutoInclude and unmapped filter/result members retain RGD3402/scripted boundaries.
- Single results and bounded patch/create batches keep complete keys and input order. Hidden
  saved roots fail after persistence; batches never silently shorten. Explicit transactions can
  roll back. Inserts/shared Save are not filtered or turned into permission/constraint checks.
- Related helper translations require the command's own UseInMemoryTranslations. Entity read
  flags alone enable neither commands nor saved graphs. Scalar results need no read backend and
  unused permissions/absent filters/disabled backends add no graph filter infrastructure.
- Graph mutations/reconciliation, navigation-based write selection/setters and native returning/
  upserts remain separate. Production, contracts/API/cache and unconfigured output stay unchanged.
  Successful savedModelQueryFilters receipts record permissions, order, predicates and failure scope.
- [MemorySavedFilters](../../samples/ConfigurationExamples/MemorySavedFilters/README.md) pairs
  [SavedShelfRepositoryDesigner](../../samples/ConfigurationExamples/MemorySavedFilters/Configuration/SavedShelfRepositoryDesigner.cs)
  with four outputs and an EF/model-free consumer. The index now has fifty-seven apps, forty-two
  testing examples and forty fake-only consumers.

See [permissions, saving order and deliberate boundaries](../implemented/testing/INMEMORY-SAVED-FILTERS.md).

Focused verification: 37 new cases and 16 existing write-boundary cases passed. This covers
all layouts/both store shapes, owner/related permission separation, output/API stability, unused
settings, mapped member validation, translations, null safety, ordered batches, shared saving,
post-save failures, explicit rollback, bounds/cancellation and retained reconciliation/native bounds.
Four SQLite cases compare actual generated repositories with both fixture store shapes.

- Full Release solution build: zero warnings/errors. All fifty-seven designer apps were generated,
  compiled against target input-only references, built and executed.
- All 221 previous generated artifacts are byte-identical; this example adds four (225 total).
  All forty fake-only dependency inventories exclude EF/models/designers/generation tooling.
- All 134 Markdown documents and 2,179 local links resolve. Tracked and all 48 feature-relevant
  file whitespace checks passed.
- Final no-write freshness/compilation and approved API-baseline checks passed; normal contract
  binaries were restored and the focused consumer passed again.
- Complete bounded-parallelism regression: **2,992 passed** (2,497 Tool, 387 Testing, 96 EF runtime,
  10 attribute generator, 2 attributes). The 24 live SQL Server cases remain skipped without
  REPOGEN_SQLSERVER_TEST_CONNECTION.

## Optional model-filtered scalar native returning (2026-10-14)

Delivered the bounded F15.4/IM08 immediate-result extension:

- Existing SimulateImmediateWrites and per-command UseInMemoryModelQueryFilter together permit
  portable final-model scalar filters on Update/Delete ReturnsMany. Both stay off by default;
  no additional flag/read backend or simple-user infrastructure is required. Entity read settings
  do not authorize commands, and production returning capability remains mandatory.
- Filters apply to saved pre-mutation selection before visibility, predicates and versions.
  Update returns matched post-update values; Delete returns matched pre-delete values. Results are
  not fresh filtered queries, so a Deleted=true update retains its returned rows while fresh reads
  hide them. AffectedRows equals Items.Count; order and absence-versus-conflict distinctions are not inferred.
- Existing original-row setters, explicit versions, complete public maps, pre-commit fixture
  preparation, pending-state isolation and checkpoint semantics remain. Native token changes can
  conflict with a later tracked Save; unchanged tokens still merge assigned columns without
  treating model filters as shared Save constraints. Copies/baselines remain unfiltered.
- Context/captured/shadow/unsupported filters and unregistered helpers retain boundaries. Exact
  scalar translations require per-command permission without production SQL client fallback.
  Production RGD1306 checks still reject shadow state/navigation returns before fixture coverage.
- Filtered atomic insert/upsert and graph/provider semantics remain separate. Contracts/API/cache,
  production and unconfigured output remain unchanged. New receipts distinguish matched native
  values from fresh saved-result filtering; no relational equivalence is claimed.
- [MemoryNativeFilters](../../samples/ConfigurationExamples/MemoryNativeFilters/README.md) pairs
  [NativeNotebookRepositoryDesigner](../../samples/ConfigurationExamples/MemoryNativeFilters/Configuration/NativeNotebookRepositoryDesigner.cs)
  with four outputs and an EF/model-free consumer. The index now has fifty-eight apps, forty-three
  testing examples and forty-one fake-only consumers.

See [selection, native values and remaining boundaries](../implemented/testing/INMEMORY-NATIVE-FILTERS.md).

Focused verification: 34 new cases and 15 retained scalar/write-boundary cases passed, including
all layouts/both store shapes, separate permissions, output/API stability, filter-changing returns,
delete values, visibility bypasses, tokens, pending conflicts, shared Save, preparation failures,
cancellation, baselines and explicit rollback. The obsolete returning-override assertion is replaced
by positive coverage; filtered atomic validation remains. Four SQLite cases compare actual generated
repositories with both fixture store shapes.

Final verification:

- Clean Release solution build: zero warnings/errors.
- All fifty-eight example applications compiled and ran, including the EF/model-free native-filter consumer.
- Input-only compiler freshness check passed for all 229 generated artifacts; the normal contracts
  assembly was restored afterwards. All 225 prior artifacts remain byte-identical; four are new.
- All forty-one fake-only consumer dependency inventories remain EF/model/designer/tool-free.
- Full bounded-parallelism regression run: 3,025 passed, zero failed; twenty-four SQL Server cases
  skipped without REPOGEN_SQLSERVER_TEST_CONNECTION. All five suites completed successfully.
- Documentation audit: 136 Markdown documents, 2,211 local links, none broken. All forty-one
  feature files and the tracked diff passed whitespace checks; the approved API baseline remains compatible.

## Explicit fixture model-filter context bindings (2026-10-14)

F15.3/F15.4/IM06/IM08 now include optional exact context-member recipes:

- `HasInMemoryModelFilterContext<TContext, TKey, TValue>(contextValue, keyValue)` binds a direct
  public instance context scalar property/field to a portable typed-key expression. The designer
  stores immutable registrations and rejects duplicate member symbols. Generation validates the
  actual context/key types and portable scalar shape without evaluating either lambda or a
  captured getter; `RGD4701`/`RGD4702` distinguish declaration and recipe failures.
- Read roots, typed From roots, ordinary declared filtered collections/locally guarded optional
  references, scalar tracked/batch commands, immediate scalar counts/native DTOs and fresh
  saved DTO reads reuse the binding under their existing independent permissions. The operation
  key is selected once after automatic guards/cancellation and reused for the fixture partition
  and all participating filters. Cold streams select it at enumeration; scripts retain priority.
- Production, contracts, cache/API identity and configurations without bindings remain unchanged.
  Successful receipts record only used context/member/key recipes. Missing/unused/ignored filters
  and disabled backends emit no binding locals/receipts. Partial binding never drops an unbound
  context condition or executes captured service behavior.
- Filtered required references, skip graphs, graph reconciliation, atomic insert/upsert, shadow/
  inheritance/loading and provider semantics retain their existing boundaries. No fixture
  DbContext, tenant service, SQL translation or database-equivalence inference is added.
- [MemoryContextFilters](../../samples/ConfigurationExamples/MemoryContextFilters/README.md)
  pairs [TenantNotebookRepositoryDesigner](../../samples/ConfigurationExamples/MemoryContextFilters/Configuration/TenantNotebookRepositoryDesigner.cs)
  with its exact contracts/production/fixture/manifest outputs and an EF/model-free consumer.
  The index now has fifty-nine apps, forty-four testing examples and forty-two fake-only consumers.

See [the binding guide](../implemented/testing/INMEMORY-MODEL-FILTER-CONTEXTS.md) for configuration,
diagnostics, permissions and selection/save/result timing.

Verification:

- Thirty-one new tests and thirty-five retained filter cases passed (sixty-six focused cases),
  including all three layouts/both store shapes, throw-on-read context getters, direct scalar keys,
  public fields, portable scalar recipes, rejected captures/clocks/helpers/identity, independent
  permissions, partial binding, unchanged output/API, joined/navigation/page reads, tracked/batch/
  immediate writes, post-save missing results, partition isolation, cold snapshots and script/
  validation/cancellation precedence. Four of these cases compare actual generated SQLite
  repositories with both fixture store shapes.
- Clean Release solution build: zero warnings/errors. All fifty-nine apps independently regenerated,
  passed target-framework compilation validation, compiled and ran; the focused consumer was run
  again after restoring the normal generated contracts assembly.
- Input-only compiler freshness check passed for all 233 generated artifacts, with the approved API
  baseline compatible. All 229 existing outputs remain unchanged; four are new. A separate hash
  audit confirmed the 222 existing C#/manifest artifacts are byte-identical.
- All forty-two fake-only dependency inventories remain EF/model/designer/generator-free.
- Full bounded-parallelism regression run: 3,056 passed, zero failed; twenty-four SQL Server cases
  skipped without `REPOGEN_SQLSERVER_TEST_CONNECTION`. All five suites completed successfully.
- Documentation links/anchors and changed/new file whitespace checks passed. The wiki exporter
  includes the new implemented guide; publication/remotes were not changed.

## Scalar model-filtered atomic fixtures (2026-10-15)

F15.4/IM08 now includes separately opted-in scalar insert-if-absent/upsert fixtures:

- The existing command `SimulateImmediateWrites()` and `UseInMemoryModelQueryFilter()` settings
  jointly permit portable model-filtered atomic operations. No default switch, service-side DSL,
  public port mutation or production atomic reload bypass was added.
- Conflict probes inspect all saved rows, including deleted/hidden rows. Inserts and their DTOs
  are unfiltered. Existing-row upserts apply model/designer predicates and explicit tokens; saved
  primary/conflict keys are retained. Designer visibility is not reapplied to updated result reads.
- Successful updates recheck the model filter after setters/value factories and before fixture
  encoding/result/commit, even without configured DTO returns. A now-hidden model result fails
  rather than becoming a native changed-row result or a tracked post-save result. Inputs are
  detached; false/throwing/reentrant filter checks and cancellation preserve rows/pending work.
- Existing ordinary conflict equality, scalar/public map/value checks, original-row setters,
  pending-state/version semantics and checkpoint rollback remain. Scalar helper translations need
  their own command permission; context-member bindings can supply typed partition inputs.
  Unbound captures, graph/shadow/inherited/loading/custom-provider shapes remain explicit boundaries.
- Real operation-owned SQL transactions roll back failed filtered reloads. Caller-owned SQL
  transactions can retain the update until caller rollback; fixture failures remain pre-commit.
  This difference is documented and tested, not hidden behind a database-equivalence claim.
- [MemoryAtomicFilters](../../samples/ConfigurationExamples/MemoryAtomicFilters/README.md) pairs
  [FilteredReservationRepositoryDesigner](../../samples/ConfigurationExamples/MemoryAtomicFilters/Configuration/FilteredReservationRepositoryDesigner.cs)
  with exact contracts/production/fixture/manifest artifacts and an EF/model/provider-free consumer.
  It demonstrates composite unique conflicts, filtered inserts/updates/result failures, retained
  keys/staging, stale tokens and checkpoint restoration. The index has sixty apps, forty-five
  testing examples and forty-three fake-only consumers.

See [branch, result-read and rollback semantics](../implemented/testing/INMEMORY-ATOMIC-FILTERS.md).
Successful receipts separately describe unfiltered conflict/insertion and filtered update/result
branches; scripts keep priority. Missing filters, disabled backends and unconfigured output are unchanged.

Verification:

- Thirty-nine new generator/fixture cases and eight new runtime cases passed. The focused generator
  run included one retained permission case (forty total); the runtime run included twenty-five
  retained atomic cases (thirty-three total). All layouts/both store shapes, independent permissions,
  captured/filter/helper boundaries, context-key bindings, partition isolation, version/pending
  collisions, factory/filter/result order, cancellation, checkpoints and explicit scripts are covered.
- Eight SQLite cases compare actual generated repositories with both store shapes: filtered
  conflicts/insertion/updates, model versus designer visibility, filter-changing result failure
  with/without DTO returns, owned rollback and deliberate caller-owned transaction-state differences.
- Clean Release solution build: zero warnings/errors. All sixty example apps independently
  regenerated, passed target-framework compilation validation, compiled and ran; the focused
  consumer ran again after restoring the normal generated contract assembly.
- Input-only compiler freshness check passed for all 237 generated artifacts, with the approved
  API baseline compatible. All 233 existing outputs remain unchanged; four are new. A separate hash
  audit confirmed all 226 existing C#/manifest artifacts are byte-identical.
- All forty-three fake-only dependency inventories remain EF/model/designer/generator-free.
- Full bounded-parallelism regression run: 3,103 passed, zero failed; twenty-four SQL Server cases
  skipped without `REPOGEN_SQLSERVER_TEST_CONNECTION`. All five suites completed successfully.
- Documentation links/anchors and changed/new file whitespace checks passed. The wiki exporter
  includes the new implemented guide; publication/remotes were not changed.

## Model-filtered explicit many-to-many fixtures (2026-10-15)

- Existing `UseInMemoryModelQueryFilter(includeNavigations: true)` permission now also covers
  explicitly mapped CLR many-to-many endpoints and join tables. Each filtered dataset still
  opts in independently. Commands returning fresh saved DTOs retain their separate graph consent;
  read permission does not grant writes/results permission. Simple/default configurations gain
  no switch, dependency or generated infrastructure.
- One coordinated detached snapshot maps each participant once, then filters each dataset before
  declared loading. Filtered required targets omit links rather than source owners; optional
  targets keep the existing explicit leading non-null guard or null-preserving DTO policy.
  Accessed inverse edges, From roots, nested projections, pages and cold streams reuse that model.
  Model filters and named designer visibility bypasses remain independent.
- `LoadFilteredManyToMany` retains unfiltered principal keys to distinguish a filtered endpoint
  from a truly missing required row. Hidden principal duplicates and visible join-pair duplicates
  still fail before assignment; filtered-out join rows do not participate. Explicit component
  comparison and cancellation apply throughout. Existing unfiltered loading is unchanged.
- Fresh single/bounded-batch saved projections reuse the filtered result graph after the shared
  Save, preserving complete keys/input order and post-save failure semantics. No graph persistence,
  relationship fixup into saved rows, automatic rollback or SQL constraint guarantee is inferred.
- Conditional join receipts identify source/target/join filters, omitted required links, optional
  null policy and unfiltered-principal/visible-pair validity. Production/contracts/API output and
  disabled/absent-filter cases remain unchanged. Typed scripts keep priority; strict coverage
  retains RGD3302/RGD3402 and all mapping/capture/provider/null-safety guards.
- [MemoryFilteredLinks](../../samples/ConfigurationExamples/MemoryFilteredLinks/README.md) pairs
  `FilteredCatalogRepositoryDesigner` with its exact contracts, production repositories, fixture
  and manifest. Its EF/model/designer-free consumer demonstrates composite article keys, a
  surrogate join key, archive filters, inverse reads, related visibility, paging, bypasses,
  detached lists, fresh saved DTOs and baseline restore.
- Fifty new tests cover loader validation/comparison/cancellation and generated behavior across
  both store shapes/layouts, independent permissions, typed context bindings without getter
  evaluation, partition isolation, scripts, invalid saved-result graphs and real generated SQLite
  contracts. Existing required ordinary-reference, implicit/shared-join, AutoInclude, mapper,
  provider-expression and graph-write boundaries remain deliberate.

Verification for this slice:

- Clean Release solution build: zero warnings/errors. All sixty-one apps regenerated, target-reference
  compiled and ran; all forty-four fake-only dependency inventories remain EF/model/designer/generator-free.
- All 241 managed artifacts passed deterministic freshness checks, including the existing API
  baselines. The previous 230 source/manifest artifacts stayed byte-identical; only the new example's
  four source/manifest outputs were added. Normal contract assemblies were restored after bootstrap.
- Full bounded-parallelism regression run: 3,153 passed, zero failed; twenty-four SQL Server cases
  skipped without `REPOGEN_SQLSERVER_TEST_CONNECTION`. All five suites completed successfully.
- Documentation links/anchors and changed/new file whitespace checks passed. The local wiki export
  includes the implemented guide; no remote publication, settings or credentials were changed.

## Model-filtered fixture child/link reconciliation (2026-10-15)

- `SimulateCollectionWrites(includeModelQueryFilters: true)` separately opts designed child/link
  datasets into portable final-model scalar filters. The additional permission is off by default;
  parent selection, entity reads, helper translations and fresh saved graphs retain independent
  consent. Parameterless/disabled simulation clears this additional permission.
- Candidates are scoped by complete owner FK, then model filter and existing child visibility,
  before bounds, matching and missing-item Keep/Reject/Delete policies. Root visibility bypasses
  do not bypass child policies. Hidden/filtered owner-scoped pending rows still block reconciliation.
- Insertions and shared Save are not validated against query filters. Explicit generated IDs see
  all saved/reserved rows; approved updates may become hidden. Fresh `ReturnsSaved` DTOs take a
  separately permitted filtered snapshot after Save, retaining post-save failure semantics.
- Child-only context/key bindings resolve the operation key once and share the selected partition.
  Complete public maps, helper permissions, versions, conditional collections, rollback, strict
  guards and script priority remain. Unsupported captures/loading/graphs retain overrides.
- Filtered join reconciliation does not make endpoint filters into FK or authorization constraints.
  SQLite cases preserve the deliberate difference between primary-key fixture validity and SQL
  unique logical-key constraints. No general graph persistence or provider guarantee is inferred.
- [MemoryFilteredCollections](../../samples/ConfigurationExamples/MemoryFilteredCollections/README.md)
  pairs `FilteredChecklistRepositoryDesigner` with four exact contract/production/fixture/manifest
  outputs. Its EF/model/designer-free consumer demonstrates independent permissions, Stage/shared
  Save, archived/hidden rows, seed-aware IDs, pending-state rejection and explicit rollback.

See [configuration, operation phases and boundaries](../implemented/testing/INMEMORY-COLLECTION-FILTERS.md).
Conditional collection-filter receipts describe accepted predicates and their selection/insert/Save/
result boundaries. Production/contracts/API/cache output and simple/default generation are unchanged.

Verification:

- Fifty-four focused tests passed across entity/public store shapes, generated layouts, independent
  permissions, supported filters/bindings/recipes, explicit links, versions, pending state, conditional
  inputs, scripted overrides and unchanged output. Shared cases execute actual generated SQLite
  repositories, including the explicitly documented unique-constraint difference.
- Full bounded-parallelism regression run: 3,205 passed, zero failed; twenty-four SQL Server cases
  skipped without `REPOGEN_SQLSERVER_TEST_CONNECTION`. All five suites completed successfully.
- Clean Release solution build: zero warnings/errors. The focused consumer ran again after
  restoring the normal generated contract assembly. The local wiki export includes the new guide.
- All sixty-two example apps regenerated, passed target-reference compilation validation, built and
  ran. All 245 managed generated artifacts passed freshness checks; the approved API baseline remains
  compatible. Existing managed outputs remain unchanged; four belong to the new example.
  Normal contract assemblies were restored after input-only compilation validation.
- All forty-five fake-only dependency inventories remain EF/model/designer/generator-free. The index
  now contains forty-seven testing examples. Documentation links/anchors, whitespace and private-name
  checks passed; no remote publication, credentials or repository settings were changed.

## Shared required additional link-reference columns (2026-10-15)

- Existing AllowSharedReferenceColumns permission now also admits explicitly named required
  additional FK roles that share columns with ownership, the target or other required references.
  It remains off by default; optional overlaps retain their own unsupported boundary.
- Complete Add-only FK maps assign each shared scalar once. New owner-shared values must agree
  before binding, including components outside the complete target matching key. Actual principal
  aliases/order and alternate keys are retained. A reference still needs a non-owner component and
  compatible CLR types; property protection and immutable owner/target/additional keys remain.
- Target/owner matching-key validation still applies to all supplied target keys before loading.
  Additional values are instead checked on new links only, before matched child versions or mutation.
  Add is never evaluated on matched rows, and their additional inputs remain ignored, not reassigned.
- Generated production and fixture guards share these boundaries. Fixture guards precede generated
  values and preserve once-only creation, cancellation, earlier pending work and multi-collection
  rollback. Separate fixture/backend/read/filter/result permissions remain unchanged.
- Conditional receipts identify actual overlapping columns, other roles, owner principal properties,
  validation timing and the database-only constraint boundary. No endpoint writes, tenancy,
  authorization, FK simulation or general graph persistence is inferred.
- [MemorySharedReferences](../../samples/ConfigurationExamples/MemorySharedReferences/README.md)
  pairs `SharedAuditRepositoryDesigner` with four exact generated outputs. A global target key and
  partition-scoped actor make the Add-only consistency guard visible. The EF/model-free consumer
  demonstrates immutable matches, fresh DTOs, Stage/shared Save, rollback and baseline restore.

See [configuration, phase distinctions and limits](../implemented/writes/SHARED-ADDITIONAL-REFERENCES.md).
The current index has sixty-three apps, forty-eight testing examples and forty-six fake-only consumers.
All 245 pre-existing managed outputs remain byte-identical; the new example adds four outputs.

Verification:

- Thirty-six new generated-operation cases cover every output layout, both fixture shapes, aliased/
  reordered owner keys, alternate/composite references, shared columns outside the matching key,
  immutable matches, independent fixture permission and scalar/protection/completeness guards.
  Two new positive compile cases replace the former blanket overlap rejection; implicit optional
  references remain guarded. Ten new shared contracts execute actual generated SQLite operations,
  including failed FK/check saves and fixup restoration with automatic detection enabled/disabled.
- Six new runtime cases check once-only initializer/guard ordering, rejection/throw/cancellation,
  absence of early generated-value allocation and restoration of earlier collection/pending work.
  Focused runs passed 116 generated/reference cases and 24 runtime/collection cases; the full run
  additionally includes the final no-Add/no-unused-guard case.
- Clean Release solution build: zero warnings/errors. All sixty-three example apps built and ran;
  the focused consumer passed again after normal contract assemblies were restored.
- All 249 artifacts passed target-reference compiler validation and deterministic freshness checks;
  the approved API baseline remains compatible. Hash comparison confirmed all 245 existing artifacts
  are unchanged. All forty-six fake-only dependency inventories remain EF/model/designer/generator-free.
- Full bounded-parallelism regression run: 3,248 passed, zero failed; twenty-four SQL Server cases
  skipped without `REPOGEN_SQLSERVER_TEST_CONNECTION`. All five suites completed successfully.
- Documentation links/anchors, whitespace and private-name checks passed. The local wiki export
  includes the new implemented guide; no publication, credentials or remote settings were changed.

## Owner-shared optional additional references (2026-10-15)

- OptionalAdditionalReference now composes with explicit AllowSharedReferenceColumns when every
  optional overlap is also an owner column. Required ownership stays complete; independent nullable
  components define absent/present values and reject partial creation. Independent non-nullable or
  generated components, non-owner optional overlaps and optional matching targets remain unsupported.
- Add explicitly supplies every FK component once. Shared owner values are checked before binding,
  even for absent references, including columns outside the matching target key. Required and optional
  roles can share owner prefixes without inferring dependencies between their independent keys.
- Add is evaluated only for new links, before matched child versions/mutation. Matched additional
  input stays ignored and FK values stay immutable. Existing partial-null rows are not normalized:
  the all-or-none creation policy is stricter than EF's ordinary any-null composite read matching.
- Existing owner guards, bounds, pending state, conditional inputs, versions, Stage/shared Save,
  rollback/fixup and independent fixture/read/result permissions are reused. No runtime library
  switch, tenancy/authorization policy, endpoint mutation or database-constraint simulation is added.
- Conditional optional receipts expose presenceColumns separately from always-required owner
  consistency. Existing disjoint optional and required shared-reference output remains byte-identical;
  an unused shared-column permission adds no output.
- [MemoryScopedOptional](../../samples/ConfigurationExamples/MemoryScopedOptional/README.md) pairs
  `ScopedReviewRepositoryDesigner` with four exact generated outputs. Its EF/model-free consumer
  demonstrates independent nullable components around a shared partition, absent cross-owner/partial
  rejection, immutable matches, fresh versioned DTOs, Stage/shared Save, rollback and baseline restore.

See [configuration and presence/ownership boundaries](../implemented/writes/OWNER-SHARED-OPTIONAL-REFERENCES.md).
The index now has sixty-four apps and forty-nine testing examples. All 249 previous managed
artifacts remain unchanged; four belong to the new example.

Verification:

- Forty-seven new cases passed within a focused run of 134 reference cases. They cover all output
  layouts and both fixture shapes, interleaved/composite/alternate keys, required/optional shared
  roles, independent consent, absent/present/partial values, owner checks, immutable matches,
  expected versions, bounds, conditional omission, shared saves and rollback. Fourteen new SQLite
  cases compare actual generated operations and cover preexisting partial-null values plus FK/check
  failures and optional fixup restoration with automatic change detection enabled/disabled.
- Clean Release solution build: zero warnings/errors. All sixty-four registered examples built and
  ran, including the new public-data consumer. All forty-seven fake-only dependency inventories remain
  EF/model/designer/generator-free.
- All 253 managed artifacts passed target-reference compilation validation and deterministic
  freshness checks; the approved API baseline remains compatible. Hash comparison confirmed all
  249 existing outputs are byte-identical. Normal contracts were restored before the final build/run.
- Full bounded-parallelism regression run: 3,295 passed, zero failed; twenty-four SQL Server cases
  skipped without `REPOGEN_SQLSERVER_TEST_CONNECTION`. All five suites completed successfully.
- Documentation links/anchors, changed/new whitespace and private-name checks passed. The local wiki
  exporter includes the new implemented guide; no remote publication/settings/credentials were changed.

## Explicit nested DTO helper recipes (2026-10-15)

- Per-registration allowNestedData extends HasInMemoryProjectionTranslation with independent nested
  public DTOs and materialized arrays/lists. One-to-four scalar inputs, exact overload/fixed-slot
  matching, retained inputs, acyclic aliases and separate command translation consent remain.
- Model-aware bounded metadata inspection checks every public result member without running
  constructors/getters/source helpers. Entity-bearing/recursive/delegate/framework/unsupported
  collection types are rejected, including nested/generic shapes. Sixteen data levels and 128 DTO
  types bound inspection; cached type heights still enforce the full path depth.
- Approved recipes use public DTO construction/assignments, array literals, standard List initializers
  or constructors, Array.Empty and portable pipelines ending in ToArray/ToList. Deferred result
  collections, implicit member/collection mutation, private constructors and static writes fail.
- Flat/scalar registrations do not acquire nested permission from a shared registry. Production
  expressions/contracts/cache/API remain unchanged; a fixture recipe does not make an opaque mapper
  SQL-translatable or enable entity graph mapping, related inputs/loading, store maps or graph writes.
- Used receipts identify ExplicitNestedDtoAndMaterializedCollectionConstruction. Unused/disabled
  recipes emit nothing. The renderer handles approved List initializers and literal string receivers
  without evaluating them or relaxing captured-state checks.
- [MemoryNestedProjections](../../samples/ConfigurationExamples/MemoryNestedProjections/README.md) pairs
  BriefingsRepositoryDesigner with exact contract/production/fixture/manifest outputs. Its internal
  scalar model and EF/model-free consumer demonstrate named projection reuse, nested facts/words,
  fresh collections, pages, cold snapshots, version conflicts and fresh saved DTOs.

See [configuration and result-data boundaries](../implemented/testing/INMEMORY-NESTED-PROJECTIONS.md).
The index now has sixty-five applications and fifty testing examples. Broader entity/deferred
mapper graphs and provider policies remain explicit extensions.

Verification:

- Release solution build: zero warnings/errors. All 257 managed artifacts passed .NET 8 target-reference
  compilation validation and deterministic freshness checks; the approved API baseline is compatible.
  All 253 previous artifacts are byte-identical. Normal generated contracts were restored before the
  final solution build.
- All forty-eight fake-only dependency inventories exclude EF/models/designers/generation tooling.
- All sixty-five registered example applications ran successfully, including the new nested result
  consumer. Full bounded-parallelism regression run: 3,348 passed, zero failed; twenty-four SQL Server
  cases skipped without `REPOGEN_SQLSERVER_TEST_CONNECTION`. All five suites completed; all fifty-three
  new nested-projection cases passed.
- Focused coverage includes every layout and both fixture shapes, nested/null/struct data, scalar and
  projection aliases, List/array/materializer recipes, bounded type inspection, captured/private/entity
  safeguards, separate command consent, saved batches and Stage/shared Save/baseline behavior. SQLite
  cases compare actual generated final projections/commands and prove opaque SQL predicates still fail.
- Documentation links/anchors, changed/new whitespace and private-name checks passed. The local wiki
  exporter includes the new implemented guide; no remote publication/settings/credentials were changed.

## Delivered per-command native shadow result slice (2026-10-15)

- `IncludeShadowProperties(bool)` explicitly permits detached mapped shadow values on one native
  `ReturnsMany` command. It is off by default, independent of provider selection, and does not
  enable tracked/count/insert/upsert paths or fixture shadow-state simulation.
- Literal, exact-type `EF.Property<T>(row, "Name")` result reads become generated scalar-buffer
  reads. Native SQLite/SQL Server columns retain model converters, nullable/binary/computed values
  and ordinary CLR primary/composite keys. Shadow/immutable/generated assignment targets,
  backing-field/indexer mappings and opaque entity/navigation result helpers remain rejected.
- Optional `IRepositoryReturningStateWriter` extends, rather than replaces, existing custom
  writers. Bundled providers share detached row materialization; ordinary commands keep their old
  writer path with no scalar dictionary allocation. State/projection/validation classes live in
  focused files, including extracted existing runtime types.
- One shared executor preserves projection-before-commit, savepoint rollback, shared commit
  observers and cache invalidation. It never calls SaveChanges, attaches entities or synchronizes
  pending tracked CLR/shadow values. Results do not imply a caller-owned transaction is committed.
- Strict fixtures report uncovered shadow commands even when immediate simulation is enabled.
  Typed override acknowledgements/scripts remain available, including on empty stores; unsupported
  non-strict calls do not return false empty/success results.
- [ShadowReturning](../../samples/ConfigurationExamples/ShadowReturning/README.md) pairs
  AuditDocumentsRepositoryDesigner with four exact contract/production/manifest/API artifacts.
  Its direct-context SQLite consumer exercises computed/default values, parameterized text,
  model filters, versions, pre-delete values, tracked isolation and explicit rollback. No tenancy,
  cache, retry or testing backend is required. The index has sixty-six apps and fifty testing examples.

See [configuration, custom writers and deliberate boundaries](../implemented/writes/RETURNING-SHADOW-VALUES.md).
The 257 earlier managed artifacts are byte-identical; all 261 artifacts passed .NET 8 target-reference
compilation and deterministic freshness checks. The Release solution builds with zero warnings/errors.

Verification:

- All sixty-six registered applications ran successfully. All forty-eight fake-only dependency
  inventories still exclude EF, models, designers and generation tooling.
- All five regression suites executed: 3,390 passed, twenty-six live SQL Server cases skipped, and
  one old diagnostic-text assertion failed because it still described shadow state as unconditionally
  unsupported. That assertion/comment was updated to verify the required explicit permission.
  The complete returning/native-filter rerun then passed 119 cases, with seven SQL Server skips,
  including all forty-three new shadow-result cases. No production code changed after the full run.
- Coverage includes all output layouts, both providers' generated code, exact named-property guards,
  unsupported mapping/writer checks, detached null/enum/binary/computed values, global/designer
  filters, composite keys, expected versions, pending-state isolation, empty/cancelled results,
  standalone/materialization failures, shared savepoints/rollback, cache and typed fixture overrides.
- Documentation links/anchors, changed/new whitespace and private-name checks pass. Wiki validation
  uses a local export only; no remote publication, settings or credentials are changed.

## Delivered typed mapped-property bindings slice (2026-10-16)

- Explicit designer-only `HasAccessor<T>("ModelName")` bindings add named/shadow scalar lookups
  without inferring other shadow keys/indexes or adding runtime property-name arguments. Exact
  model CLR types, safe generated identifiers, custom method names and quoted metadata are
  validated before output. Existing adapters/converters and unique metadata are reused.
- Named `ExpectVersion<T, TRequest>("ModelName", request => request.Token)` binds caller originals
  on root commands; batch items and child reconciliation have matching input-only overloads.
  Tracked operations preserve Stage/Save and EF original-state checks; immediate/native writes
  include token predicates. No token advancement, return permission, stale-client retry or conflict
  resolution is inferred.
- A dedicated `RepositoryExpectedVersionDesign` separates original-value conditions from update
  assignments. Model validation, expression inventory, tracked key literals, deterministic
  manifests and optional fixture capability checks support the new binding.
- Named public CLR tokens remain eligible for existing entity-shaped/mapped-public fixture stores.
  Shadow/non-public token state requires typed overrides; strict mode rejects unacknowledged
  coverage. Production metadata access does not grant fixture shadow-state simulation.
- [MappedAccessors](../../samples/ConfigurationExamples/MappedAccessors/README.md) maps its
  AliasDirectoryRepositoryDesigner to three exact selector/projection/manifest outputs.
  [ShadowVersions](../../samples/ConfigurationExamples/ShadowVersions/README.md) maps its
  VersionedNotesRepositoryDesigner to four contract/implementation/manifest/API artifacts and
  exercises tracked, native and bounded batch writes through EF-free application ports.
  Both use direct contexts and simple defaults, without caches, tenancy or retry infrastructure.

See [configuration and deliberate boundaries](../implemented/repositories/MAPPED-PROPERTY-BINDINGS.md).

Verification:

- Release solution build: zero warnings/errors. All sixty-eight applications ran successfully.
- All five regression suites: 3,431 passed, zero failures, twenty-seven live SQL Server cases skipped
  because no test connection was configured. The forty new passing mapped-binding cases cover
  SQLite lookups, converter inputs, quoted metadata, nullable/binary/converted tokens, tracked
  Stage/Save and deletes with no-tracking context defaults, batch and child conflicts, typed
  fixture boundaries, output layouts and competing update/consume calls. The new SQL Server
  shadow-rowversion live case is included among the skips, not counted as verified database behavior.
- All sixty-eight generated bundles passed .NET 8 target-reference compilation. All 268 managed
  artifacts passed freshness checks, and the approved API baseline remains compatible. All 261
  earlier artifacts are byte-identical; only seven new example outputs were added.
- All forty-eight fake-only dependency inventories still exclude EF, models, designers and
  generator tooling. Documentation links/anchors, changed/new whitespace and private-name checks
  pass. A local wiki export validates ninety-four content pages and the grouped sidebar; no
  remote publication or external target-application changes were performed.

## Delivered native mapped-value capture slice (2026-10-18)

- Per-command `IncludeMappedProperties()` explicitly permits native returned scalar state from
  writable configured field-backed/field-only/indexer mappings, alongside CLR/shadow columns.
  It is off by default; the narrower `IncludeShadowProperties()` permission and all previous
  generated output remain unchanged. Disabling broad capture retains a separately selected
  shadow-only permission.
- `IRepositoryReturningMappedStateWriter` advertises the additional infrastructure capability.
  Both bundled writers use one shared metadata-only materialization policy with the generator,
  plus a prepared per-operation assignment plan rather than per-row metadata resolution.
  Custom ordinary/shadow-only writers fail with `RGD1308` instead of receiving a fallback.
- Opted-in result members and literal typed `EF.Property` reads use detached native scalar
  buffers, not custom entity getter behavior. Normal CLR keys/setter targets, writable instance
  materialization members, one-table/constructor restrictions and provider guards remain.
  Immutable/static fields, indexer/field/shadow keys, broader graph state and fixture parity
  are not inferred.
- The existing executor preserves projection-before-commit, owned transactions/shared savepoints,
  rollback on materialization/projection failures and pending tracker isolation. No SaveChanges,
  tracking attachment, stale-client retry or automatic token advancement is added.
- Strict fixtures require explicit override acknowledgement; unsupported non-strict calls fail
  rather than report false success. Manifests add access-kind/materialization receipts only for
  broad capture, leaving ordinary and shadow-only receipts unchanged.
- [MappedReturning](../../samples/ConfigurationExamples/MappedReturning/README.md) maps its
  CapturedNotesRepositoryDesigner to four exact contract/production/manifest/API outputs. Its
  EF-free consumer checks getter-free computed fields, indexer data, field-only versions,
  stale/hidden guards, pending-state isolation, pre-delete values and caller rollback without
  requiring caches, tenancy, retry policies or a fixture backend.

See [configuration and deliberate boundaries](../implemented/writes/RETURNING-MAPPED-VALUES.md).

Verification:

- Release solution: zero warnings/errors. All sixty-nine registered applications passed.
- All five regression suites: 3,514 passed, zero failures, twenty-eight live SQL Server cases
  skipped because no connection was configured. Thirty-six new passing cases cover real SQLite
  writes and rollback, fixed metadata/type guards, ordinary versus mapped writer capabilities,
  nullable/converted/binary state, explicit fixture boundaries and both providers' generated code
  in all output layouts. The new live SQL Server mapped-output case is a skip, not a database
  behavior claim.
- All sixty-nine generated bundles passed .NET 8 target-reference compilation; all 272 managed
  artifacts passed deterministic freshness checks and the approved API baseline remains compatible.
  The 268 previous artifacts are byte-identical; only four new example outputs were added.
- All forty-eight fake-only dependency inventories still exclude EF, models, designers and
  generator tooling. Documentation links/anchors, scoped private-name and changed/new whitespace
  checks pass. Local wiki export validates ninety-six content pages and a grouped sidebar;
  no remote publication or external application source changes were performed.

## Delivered explicit fixture shadow-read slice (2026-10-18)

F15.3/IM06-IM07 now include explicitly mapped model-only scalars in detached public-data fixture
reads, without enabling tracked shadow state or changing ordinary/simple generation.

- `MapInMemoryShadowProperty<TData, TProperty>(name, value)` requires matching public store/read
  declarations and one exact final-model shadow scalar/type. Redeclaration replaces one recipe;
  explicit removal, read-map removal and store replacement clear the relevant bindings.
- Portable recipes are inspected and rendered without invoking getters, helpers or captured
  state. Typed internal slots avoid CLR/navigation name collisions and never use metadata strings
  as C# identifiers. Exact EF.Property reads are lowered only in the fixture backend: no runtime
  reflection, EF helper, persistence entity construction or extra consumer dependency is emitted.
- Named predicates/projections/default sorts/groups/scalars/dictionaries/pages/streams/From roots
  and separately opted-in model filters/declared graphs reuse those values within their existing
  boundaries. Filtered optional references still require local guards. IgnoreQueryFilters does
  not bypass designer visibility, and each dataset supplies its own property/filter permissions.
- `RGD4901` rejects invalid declarations/recipes. Unbound, dynamic, boxed/widened or unsafe reads
  retain strict `RGD3302`/typed-script boundaries; non-strict unsupported calls fail on empty stores
  too. Shadow writes/original tokens, shadow relationship identity, native returned buffers and
  saved command results are not enabled. Provider/constraint/concurrency parity is not inferred.
- Store and per-operation receipts distinguish declared recipes from query-consumed bindings.
  Detached adapters still populate all configured slots; receipts are not column-pruning claims.
  Production contracts, SQL source, API snapshots and ordinary defaults are unchanged.
- Exact EF metadata-backed byte-array reads in From projections no longer get mistaken for opaque
  sequence factories. Arbitrary sequence-producing calls retain the existing source guard.
- [MemoryShadowReads](../../samples/ConfigurationExamples/MemoryShadowReads/README.md) maps its
  RankedNotebookRepositoryDesigner to four exact contract/production/fixture/manifest outputs.
  Its EF-free consumer checks named filtering, independent visibility, shadow ordering, counted
  pages, grouped sums, cold snapshot-stable streams and retained arrangement/baselines.

Verification:

- Release solution: zero warnings/errors. All seventy registered applications passed.
- All five regression suites: 3,544 passed, zero failures; twenty-eight live SQL Server cases
  skipped because no connection was configured. Thirty new passing cases include actual generated
  SQLite/fixture comparisons, nullable/converted/binary values, filtered collections and guarded
  optional references, all three output layouts and exact declaration/identity/capture guards.
- All seventy example configurations' generated outputs passed .NET 8 target-reference compiler
  validation, deterministic freshness and the approved API baseline. All 276 managed artifacts are
  fresh; the 272 previous artifacts are byte-identical, with four new example outputs only.
- All forty-nine fake-only dependency inventories exclude EF, models, designers and generator
  tooling. Documentation links/anchors, scoped private-name and changed/new whitespace checks pass.
  Wiki tooling passed forty-six checks. No remote publication or external application source
  changes were performed.

## Delivered unified fixture mapped-read slice (2026-10-19)

F15.3/IM06-IM07 now use one optional explicit scalar binding for public-data fixture reads:
[MapInMemoryProperty](../implemented/testing/INMEMORY-MAPPED-READS.md).

- The unified API replaces `MapInMemoryShadowProperty` and its removal method without compatibility
  aliases. Existing shadow declarations use the same model names and value recipes. Field-backed,
  field-only, ordinary CLR properties/fields and mapped indexers can now use those typed slots too.
- Exact final-model names and CLR types are required, including nullable/converted values and
  binary results. An initializer and a named binding cannot supply competing values for one EF
  scalar; `RGD4901` rejects ambiguity. Redeclarations/removal/store replacement retain explicit
  lifecycle rules. Metadata-backed readonly fields require no entity setter/materializer.
  Disabling automatic reads ignores these declarations even when separate write adapters retain
  their ordinary scalar helpers; read-only values cannot become a write-state override.
- Actual mapped members, exact constant-name EF.Property calls, typed constant-key indexers and
  object indexers explicitly cast to their exact model type lower without invoking entity behavior.
  Portable recipes are inspected, not evaluated during generation. Dynamic/unbound/wrong-type reads,
  opaque behavior, entity identity and unsafe optional-target access retain diagnostics/scripts.
- Named reads, reports/groups/scalars, default/whitelisted sorting, pages, streams, dictionaries and
  From roots reuse detached slots. Scalar model filters and declared graphs still require their
  own permissions; each dataset supplies its bindings. Neither relationship nor store identity is
  redefined by a named key read. Strict/non-strict unsupported and script-precedence rules remain.
- Manifest `propertyMappings` and operation `propertyReads` include exact model types/access kinds
  and an explicit detached-read-only scope. All configured adapters still populate their slots;
  consumed-read receipts do not promise column pruning or skip unused recipe evaluation.
- Production rendering now emits ordinary C# indexer syntax for actual indexer getters and explicit
  IndexExpression nodes. Exact typed binary indexers in From projections are treated as mapped
  scalars, not opaque sequence factories. Arbitrary sequence methods retain their guards.
- [MemoryMappedReads](../../samples/ConfigurationExamples/MemoryMappedReads/README.md) pairs its
  MappedNotebookRepositoryDesigner with four exact generated outputs. Its EF/model-free consumer
  checks field-backed stored values instead of display getters, field-only versions/deletion,
  indexer values, independent model filters, safe sorting, counted pages and baselines.
  [MemoryShadowReads](../../samples/ConfigurationExamples/MemoryShadowReads/README.md) remains a
  smaller model-only walkthrough, now using the unified API.

Verification:

- Release solution passed with analyzers enabled and no compiler/analyzer errors or warnings.
  The first attempt hit a Roslyn out-of-memory failure; limiting runtime processors to two on
  the retry resolved it. Packaging reported one offline NuGet vulnerability-feed warning (NU1900),
  not a source diagnostic. No persistent machine/build configuration was changed.
- All five suites passed: **3,573 passed**, zero failures and twenty-eight unconfigured live SQL
  Server skips. Thirty new mapped-read cases cover actual generated SQLite/fixture comparisons,
  exact metadata/types, all layouts, object/typed/binary/manual indexers, readonly fields,
  getter-free graphs/sorting, copied results, strict/script boundaries and disabled-read isolation.
  The complete focused shadow/mapped-read filter passed all sixty-five cases.
- All seventy-one applications passed. Generated outputs passed .NET 8 target-reference compiler
  validation, deterministic freshness and the approved API baseline. All 280 managed artifacts are
  fresh: four new outputs, two intentional fixture/manifest updates in MemoryShadowReads and 274
  byte-identical previous artifacts. Existing production implementations/contracts are unchanged.
- All fifty fake-only dependency inventories exclude EF, models, designers and generator tooling.
  Documentation links/anchors, scoped private-name and changed/new whitespace checks passed.
  Wiki tooling passed forty-six checks; local export validated ninety-eight documentation pages
  and the grouped sidebar. No remote publication or external application source changes were performed.

## Delivered explicit mapped fixture relationship-key slice (2026-10-19)

F15.3/IM06-IM07 now support separately permitted shadow/field/indexer/CLR scalar bindings in
detached fixture relationship matching, without inferring write or tracked-state behavior.

- `HasInMemoryNavigation(..., useMappedPropertyKeys: true)` permits exact MapInMemoryProperty
  recipes for one edge's principal and FK components. Final metadata supplies composite/alternate
  component order; an inverse needs its own permission. Defaults, declaration replacement/removal
  and disabled-backend behavior remain explicit.
- References, collections and explicit CLR many-to-many joins use typed read-row slots, including
  both join FKs. No entity getter/indexer, reflection lookup or duplicate key-expression DSL is
  emitted. Null matching, principal/pair uniqueness, comparer/converter checks, model-filter graph
  permissions, optional-target guards and visibility boundaries remain independent.
- Per-edge mappedPropertyKeys receipts identify the consumed property, type, safe slot, access
  kind and source/target/join role. Their scope is read relationship matching and uniqueness only.
  Dictionary identity, writes, original tokens and provider equivalence are not inferred.
  Suitable fresh single/batch saved DTO reads reuse these keys without permitting missing result
  members or broader mapped write state.
- Synthetic read slots now avoid unassigned mapped CLR members too, preventing accidental scalar
  or key coverage through a generated-name collision. Automatic production key/index accessors
  no longer treat indexers/private/aliased properties as ordinary CLR member names; their existing
  exact typed HasAccessor bindings remain available.
- [MemoryMappedKeys](../../samples/ConfigurationExamples/MemoryMappedKeys/README.md) pairs its
  two-table KeyedNotebookRepositoryDesigner with four exact contract/production/fixture/manifest
  outputs. Its EF/model-free consumer checks a field-backed alternate key, shadow FK, both loading
  directions, nulls, distinct dictionary identity, fresh detached results and baselines.

Verification:

- The complete 102-project Release solution build passed with zero warnings/errors. Runtime
  processor limiting was local to the verification processes; no persistent build/machine settings
  were changed.
- All five suites passed: **3,602 passed**, zero failures and twenty-eight unconfigured live SQL
  Server skips (3,076 Tool, 418 Testing, 96 EF runtime, 10 attribute generator, 2 attributes).
  All twenty-nine new cases passed, covering generated SQLite/fixture comparisons, all layouts,
  composite/alternate keys, shadow/field/indexer join components, independent inverse/filter/null/
  comparer policies, saved single/batch DTO reads, slot collisions, strict/script boundaries and
  disabled-backend isolation. The broader focused regression run passed 132 cases, with one live
  SQL Server skip.
- All seventy-two applications passed. Generated outputs passed .NET 8 target-reference compiler
  validation, deterministic freshness and the approved API baseline. All 284 managed artifacts are
  fresh: four new example outputs, no removed files and all 280 previous artifacts byte-identical.
- All fifty-one fake-only dependency inventories exclude EF, models, designers and generator
  tooling. Documentation links/anchors, scoped private-name and changed/new whitespace checks
  passed. Wiki tooling passed forty-six checks. No remote publication or external application
  source changes were performed.

## Delivered explicit mapped scalar fixture write-state slice (2026-10-20)

F15.4/IM08-IM10 now separately permit complete exact mapped scalar fixture state without
inferring EF tracker, identity, graph or provider behavior.

- MapInMemoryWrites(..., useMappedPropertyState: true) reuses exact MapInMemoryProperty bindings
  for non-identity shadow/field/indexer/CLR scalar state. Every scalar still needs one source;
  keys, unique-index members and foreign keys remain ordinary explicit assignments. Redeclarations,
  removal and disabled command/entity/backend configurations retain deliberate lifecycle rules.
- Write adapters, scalar command expressions and explicit factories lower to typed slots without
  invoking persistence constructors/getters/indexers. Full structural copy/key/encoder round trips
  still validate before any shared commit; tracked equality cannot make a lossy map acceptable.
- Original expected versions, scalar create/patch/delete, bounded root-row batches, immediate scalar/
  atomic simulation and fresh saved DTOs retain existing outcomes and Stage/shared Save semantics.
  Nullable/binary tokens keep structural checks, including original-state rechecks before shared
  commit. Collection/association reconciliation with named state remains a typed override boundary.
  Atomic production mappings retain their existing ordinary settable-CLR column restriction.
- Exact-name HasInMemoryGeneratedValue/WithoutInMemoryGeneratedValue overloads supplement ordinary
  member selectors. Model type/generation, pure recipes, sentinels, ordering and explicit create/update
  phases remain. Token advancement is never inferred from a token/property name or model SQL.
- Store/operation mappedPropertyState receipts identify exact types/slots/access kinds, the separate
  permission, structural validation and explicit-only advancement. No native returned buffer, EF
  tracker, SQL constraint, provider rowversion, race or broader graph promise is added.
- [MemoryMappedWrites](../../samples/ConfigurationExamples/MemoryMappedWrites/README.md) pairs its
  one-table RevisionNotebookRepositoryDesigner with exact contract/production/fixture/manifest outputs.
  Its EF/model-free consumer checks stale rejection, Stage/shared Save, fresh shadow revisions and
  baselines. Simple defaults, production source/contracts and API/cache shapes remain unchanged.

Verification:

- The complete 103-project Release solution build passed with zero warnings/errors. Processor
  limiting remained local to verification processes; no persistent machine/build settings changed.
- All five suites passed: **3,636 passed**, zero failures and twenty-eight unconfigured live SQL
  Server skips (3,110 Tool, 418 Testing, 96 EF runtime, 10 attribute generator, 2 attributes).
  All thirty-four new cases passed, including six actual generated SQLite/fixture contract comparisons,
  all output layouts, original shadow/nullable/binary tokens, Stage/shared Save, root batches,
  scalar/eligible atomic simulation, getter-free factories, structural round trips, rollback,
  cancellation, scripts, disabled backends and unchanged production restrictions. The broader
  focused regression set passed 206 tests, with one unconfigured live SQL Server skip.
- All seventy-three applications passed. Generated outputs passed .NET 8 target-reference compiler
  validation, deterministic freshness and the approved API baseline. All 288 managed artifacts are
  fresh: four new example outputs, no removals and all 284 previous artifacts byte-identical.
- All fifty-two fake-only dependency inventories exclude EF, models, designers and generator tooling.
  Documentation links/anchors, scoped private-name and changed/new whitespace checks passed.
  Wiki tooling passed forty-six checks. No remote publication or external application source
  changes were performed.

## Delivered explicit mapped scalar returned-value fixture slice (2026-10-20)

F15.4/IM08-IM10 now compose complete separately permitted scalar write maps, production capture
permission and per-command immediate simulation to return exact fixture scalar values.

- IncludeShadowProperties/IncludeMappedProperties alone still do not grant fixture state.
  MapInMemoryWrites(..., useMappedPropertyState: true) and SimulateImmediateWrites are independently
  required. No new global flag, CLI switch or service-side property/projection DSL was added.
- Supported Update/Delete return post-update/pre-delete mapped values, including shadow,
  field-backed/field-only/indexer and exact scalar/nullable/binary tokens. Typed projections lower
  from original designer expressions without entity getters, EF calls or native returned buffers.
- Explicit update factories, structural read/write round trips and all result construction finish
  before commit. Failure/cancellation cannot partly consume rows. Pending work is neither saved
  nor refreshed; original versions can still conflict on a later shared Save. Checkpoints/baselines
  and script precedence remain unchanged.
- Production writer/table/constructor/key/setter/projection validation is unchanged. Ordinary
  complete maps with explicit consent also work without named slots. Automatic reads remain
  independent. Unsupported shapes retain strict acknowledgements and unarranged failures.
- Additive returningPropertyState operation receipts identify explicit scalar fixture state,
  value timing, preparation, pending isolation, unspecified order and absent provider equivalence.
  Production capture receipts indicate fixtureSimulation only for separately supported operations.
- [MemoryMappedReturning](../../samples/ConfigurationExamples/MemoryMappedReturning/README.md)
  pairs its focused one-table RepositoryDesigner with four exact outputs. The EF/model-free
  consumer checks fresh shadow revisions, getter-free field values, consume results and rollback.

Verification:

- The full 104-project Release solution build passed with zero warnings/errors.
- All thirty-five new cases and eight existing capture guards passed. Fourteen cases execute actual
  generated SQLite repositories, covering scalar results, model-filter selection and mapping rollback.
- All seventy-four applications passed. Target-reference compilation, API baseline and deterministic
  freshness passed; all 288 previous artifacts are byte-identical, with four additions and no removals.
- All fifty-three fake-only dependency inventories exclude EF/models/designers/tooling. Documentation
  link/anchor, scoped private-name and whitespace audits passed. Wiki tooling passed forty-six checks.
- All five complete regression suites passed **3,671 tests**, with twenty-eight unconfigured live SQL
  Server skips (3,145 Tool, 418 Testing, 96 EF runtime, 10 attribute generator, 2 attributes).
  All thirty-five new cases passed in the full run. No remote publication or external application
  source changes were performed.

## Delivered mapped scalar fixture collection-state slice (2026-10-20)

F15.4/IM08-IM10 now compose separately permitted complete scalar write maps with existing designed
bounded collection/link reconciliation. No new global setting or service-side DSL was added.

- Only the root and explicitly selected writable reconciliation children grant command scalar
  bindings. Parent/child fields are keyed by entity type, including identically named shadow tokens.
  Ordinary parents can coexist with mapped children without dummy state declarations.
- Child create, conditional setters, expected versions, copies/merges and equality route to exact
  mapped slots. Explicit named factories, structural round trips, original-token rechecks and
  Stage/shared Save/checkpoint boundaries remain. Entity getters/indexers/constructors are not used.
- Primary/unique/matching/ownership/reference keys remain ordinary complete scalar assignments.
  Production leaf-child/explicit-link, immutable FK/reference, shared-owner, write protection and
  model-filter/visibility/read permissions are unchanged. No broader graph/provider behavior is inferred.
- Supported operations add collectionSimulation.mappedScalarState receipts, with participating
  bindings, ordinary identity, originals, explicit factories, lossless preparation and absent provider
  equivalence. Disabled/unconfigured source/API/cache outputs remain unchanged.
- [MemoryMappedCollections](../../samples/ConfigurationExamples/MemoryMappedCollections/README.md)
  pairs a two-table RepositoryDesigner with four exact outputs and an EF/model-free port consumer.
  The example uses separate shadow versions, explicit recipes, bounded reconciliation, Stage/shared
  Save, fresh saved values, baselines and checkpoints.

Verification:

- The focused regression set passed 292 cases, including all forty-three new tests and nine actual
  generated SQLite comparisons. Tests cover mapped scalar slots, originals, shared-save rechecks,
  failure isolation, explicit links, filters, disabled modes and unchanged production APIs.
- The current 103-project Release solution build passed with zero warnings/errors. All seventy-five
  applications passed. Target-reference compilation, deterministic freshness and the API baseline passed.
- All 296 managed artifacts are fresh: four additions, no removals and all 292 prior artifacts byte-identical.
  All fifty-four fake-only dependency inventories exclude EF/models/designers/tooling. All 178 Markdown
  files/local anchors and scoped new-feature whitespace/private-name audits passed. Existing unrelated
  staged blank-line warnings were left untouched. Wiki tooling passed forty-six checks.
- All five regression suites passed 3,714 tests with zero failures and twenty-eight unconfigured
  live SQL Server skips. All forty-three new cases passed in the full run. No remote publication
  or external application source changes were performed.

## Delivered explicit native atomic scalar capture slice (2026-10-20)

F11/F15.4 extend existing per-command scalar capture to InsertIfAbsent and InsertOrUpdate Returns
without a new global flag, service-side query/property DSL or simple-default dependency.

- Separate IRepositoryAtomicStateWriter / IRepositoryAtomicMappedStateWriter capabilities reuse
  detached scalar buffers. Bundled SQLite/SQL Server providers share parameterized insert/conflict,
  conversion, command-draining and materialization logic; ordinary writer contracts remain usable.
- Inserted values come from native RETURNING/OUTPUT. Updated values come from a fresh no-tracking
  immutable-conflict-key scalar query in the operation transaction, retaining model filters rather
  than stale version/selection predicates. No existing-row values escape AlreadyExists/NotMatched.
- Exact shadow/field/indexer values and mapped members bypass result getters, not approved runtime
  constructors/materializers. Ordinary keys/conflicts/assignment targets, writable materialization,
  table/trigger/provider guards and independent write protection remain. No graph/dialect inference.
- Complete separately permitted fixture scalar maps and explicit factories compose with immediate
  simulation. Saved-only probes/factories, retained staging/originals, pre-commit full round trips
  and results, scripts, cancellation, baselines/checkpoints and independent filter/read flags remain.
- Owned transactions map before commit; borrowed atomic transactions still require caller rollback
  on failure. Cancellation is checked again after mapping rather than reporting canceled success.
  This does not add the Update/Delete executor's savepoint policy or infer provider/race equivalence.
- [MemoryAtomicCapture](../../samples/ConfigurationExamples/MemoryAtomicCapture/README.md) pairs its
  one-table designer with four exact outputs and a public-port consumer without EF/model dependencies.
  Configuration/options/feature guides and roadmap receipts describe the separate permissions.

Verification:

- The first broader regression run passed 296 tests with eight opt-in SQL Server skips, including
  59 new passing cases and eleven actual generated SQLite comparisons. The complete final run
  passed all 62 new runnable cases, including final mapping-time cancellation and filter-permission checks.
- The current 104-project Release build has zero warnings/errors. All seventy-six applications,
  actual .NET 8 target-reference compilation, API baseline and deterministic freshness checks passed.
- All 300 managed artifacts are fresh: four additions, no removals and all 296 prior artifacts
  byte-identical. Fifty-five fixture-only dependency inventories exclude EF/models/designers/tooling.
- All 180 Markdown files/local links and thirty scoped new-feature whitespace/private-name checks
  passed. Existing unrelated staged blank-line warnings were left untouched. Wiki tooling passed
  forty-six checks.
- All five full regression suites passed 3,776 tests with zero failures and thirty unconfigured live
  SQL Server skips, including two added capture cases. No remote publication or external application
  source changes were performed.

## Delivered fresh atomic saved-result queries slice (2026-10-20)

F11/F15.4 extend the existing per-command ReturnsSaved mode to native InsertIfAbsent/InsertOrUpdate,
without adding a global setting, a service-side graph DSL or a simple-default dependency.

- Successful branches requery the complete saved CLR primary key with no tracking inside the
  operation transaction, before an owned commit. Existing identity wins over the insert candidate;
  stale mutation/version predicates do not constrain the result. No-op branches return no data.
- Root model filters/designer visibility and explicit bypasses apply to the fresh read. Related
  policy expressions and existing navigation review remain explicit. Shared tracked changes are
  neither flushed nor used as the result graph. Optional native scalar capture remains separate
  infrastructure materialization, not a rewrite of the fresh SQL projection to buffer reads.
- Owned result/cancellation failures roll back; borrowed transactions retain caller rollback
  responsibility and no new savepoint policy. Original exceptions differ deliberately from the
  committed-write RepositorySavedResultException used by tracked ReturnsSaved after shared Save.
- Separately opted-in fixture commands prepare/encode a write once, then capture independently
  detached unpublished root rows with saved-only related tables under one partition lock. Inverse
  collections see the new root. Copy/encoder/result/relationship/cancellation failures publish
  nothing and retain staging. Full round trips, original tokens, scripts, baselines and checkpoints remain.
- Ordinary complete keys, unique targets, write protection, existing graph-write/provider guards
  and independent read/filter/state/immediate permissions remain. This is not native graph RETURNING,
  arbitrary graph persistence or SQL locking/identity/race equivalence.
- [MemoryAtomicResults](../../samples/ConfigurationExamples/MemoryAtomicResults/README.md) links its
  two-table EnrollmentRepositoryDesigner directly to four exact outputs and an EF/model-free writer
  consumer. [Atomic saved results](../implemented/writes/ATOMIC-SAVED-RESULTS.md), option inventories,
  coverage receipts and roadmap records document the result lifecycle.

Verification:

- The focused old/new regression batch passed 193 tests, with three unconfigured live SQL Server
  skips. Fifty-one new runnable cases cover every output layout, both provider compilation paths,
  complete composite keys, mapped/shadow state, saved graphs, retained staging, failure and ownership.
  Fifteen exercise actual generated SQLite operations, with two additional direct runtime transaction cases.
- Actual .NET 8 target-reference compilation and API baseline checks passed for all seventy-seven
  examples. All 304 managed artifacts are fresh: four additions, no removals and all 300 previous
  artifacts byte-identical. The 105-project Release build has zero warnings/errors; all seventy-seven
  applications and fifty-six fixture-only dependency inventories passed.
- All five full regression suites passed 3,827 tests with zero failures and thirty-one unconfigured
  live SQL Server skips, including the added atomic saved-result case. All fifty-one new runnable
  cases passed in that full run.
- All 182 Markdown documents/local links and forty-five scoped feature whitespace/private-name
  checks passed. Wiki tooling passed forty-six checks. Existing unrelated staged blank-line warnings
  were preserved. No remote publication or external application source changes were performed.

## Delivered bounded fresh native-update results slice (2026-10-21)

F11/F15.4 add per-command ReturnsSavedMany with a required positive maximumRows bound for native
Update. There is no new preset/global switch or service-side projection/query API.

- Native updated rows supply the complete saved primary keys. A separate untracked server query
  projects scalar/DTO/relationship values before owned commit/borrowed savepoint release, without
  stale mutation/version predicates or shared Save/tracker fixup. Every directly updated root must
  have exactly one result; zero matches skip result loading and construction.
- The explicit bound aborts and rolls back excess updates, not TOP/LIMIT/truncated result success.
  It is checked after native buffering; provider parameter/expression and graph-size limits remain
  explicit. Existing balanced complete-key correlation is reused without transparent chunking.
- Root model/designer visibility and explicit bypasses are rechecked; related policies remain
  projection-owned. Missing/duplicate/hidden results, bound, DTO and cancellation failures preserve
  the existing owned transaction/borrowed savepoint rollback and real-commit observer boundaries.
  Results are fully materialized and their actual cardinality validated inside that boundary.
  Original failures are not tracked post-shared-Save RepositorySavedResultException wrappers.
- Independently opted-in fixtures prepare all updates/factories, encode once, and read one detached
  unpublished root overlay with saved-only related datasets under one partition lock. Inverse
  collections see every prepared update; failure publishes nothing and retains earlier staging.
  Mapped/shadow state, read/graph/filter/immediate permission, tokens, scripts, partitions and reset
  remain independent. No SQL locking/identity/constraint/race equivalence is inferred.
- Native writer/table/key/assignment guards are unchanged. Delete retains pre-delete scalar
  ReturnsMany; tracked/atomic lifecycles remain separate. Last result selection clears prior mode
  and bounds. Unsupported fixture shapes retain strict diagnostics/typed overrides even when empty.
- [MemoryNativeResults](../../samples/ConfigurationExamples/MemoryNativeResults/README.md) links its
  two-table QueueResultsRepositoryDesigner to four exact outputs and an EF/model-free writer/read
  consumer, demonstrating abort-on-excess, fresh graph state, stale versions and baseline/checkpoints.
  [Native saved results](../implemented/writes/NATIVE-SAVED-RESULTS.md) and manifest receipts describe
  the distinct lifecycle. Feature-specific builder/runtime/manifest code uses separate focused files.

Verification:

- The focused old/new batch passed 225 tests with two unconfigured live SQL Server skips. The final
  new-feature run passed all 63 runnable cases with one unconfigured live SQL Server skip, including
  fifteen actual generated SQLite cases and seven additional direct database runtime cases.
  Adversarial result-list enumeration and reported counts fail before commit/fixture publication.
  Both provider compile paths and output layouts, complete composite keys, mapped/shadow projections,
  strict consent, rollback and retained work are covered.
- The final 106-project Release solution build passed with zero warnings/errors. All 78 example
  applications passed; 57 fixture-only dependency inventories exclude EF, persistence models,
  designers and generation tooling, including MemoryNativeResults.
- The complete five-suite run passed 3,890 tests with 32 unconfigured live SQL Server skips and no
  failures. The tool suite passed 3,364 cases; the four runtime/generator suites passed another 526.
  Its final new-feature results are 63 passing cases and one unconfigured live SQL Server skip.
- All 78 configurations passed generation with target-reference compilation; the final no-write
  freshness check and approved Ledger API baseline passed. The 308 managed outputs add exactly the
  four new example artifacts; every previous 304-file hash is unchanged and none were removed.
- All 184 Markdown documents and 2,723 local links/anchors passed the documentation audit.
  The 46 scoped implementation/example files passed whitespace/internal-name checks, the working
  diff has no whitespace errors, and all 46 wiki-tooling checks passed.
- The final documentation audit and local wiki export are part of this verification; no remote
  publication is part of this slice.

## Delivered explicit mapped command setters slice (2026-10-21)

F09/F15.4 add explicit typed final-model setter targets to scalar tracked Patch and ordinary
set-based Update, without adding property-name inputs to services or changing presets/default output.

- SetProperty<T>(name, value) supports typed constants, current-row values and typed requests.
  SetPropertyWhen adds conditional tracked assignments. Shadow, field-backed/field-only and indexer
  state use EF entry CurrentValue for tracked writes and translated EF.Property targets for Update.
  Entry access follows the model's configured access mode; the field-access example avoids its
  behavioral getter. This is not a general getter/mapper interceptor.
- Tracked named value recipes bind literal EF.Property and typed indexers to this row's exact
  scalar metadata; unknown/dynamic/foreign/mismatched reads fail generation. Current/original state,
  earlier dirty values, Stage/shared Save and command-owned conflict/failure restoration retain the
  existing executor boundary. Set-based writes do not fetch entities or flush pending work.
- Named ProtectWrite<T> and AllowWrite<T> bind exact types too. Protection stays closed-mode and
  design-time, not authorization. Named token assignment and original-version checking remain
  separate; no generated token advancement is inferred. Redeclaration/disable/member replacement
  clears stale typed protection metadata.
- Keys, foreign keys, unique identity, generated/read-only/init targets and owned/shared/inherited
  shapes remain excluded. Native returning, atomic, Create, batch/graph setters retain their guards.
  RGD1311/RGD1312 and optional mappedUpdateTargets receipts describe this bounded capability.
- Fixtures independently require complete public-data property/write-state bindings for non-public
  targets. Scalar saved DTOs do not require the automatic read backend. Immediate simulation remains
  command-owned and saved-only; scripts, original tokens, pending writes, checkpoints and baselines
  retain their existing behavior. Invalid maps fail; unsupported operations still require typed
  overrides, even when empty. No EF tracker, constraint, generated-value or provider parity is inferred.
- [MappedSetters](../../samples/ConfigurationExamples/MappedSetters/README.md) links its one-table
  EditableNotesRepositoryDesigner to four exact outputs and an EF/model-free port consumer.
  [Mapped command setters](../implemented/writes/MAPPED-COMMAND-SETTERS.md) explains configuration,
  access-mode semantics, lifecycle/identity guards and independent fixture consent. Setter-model
  construction was extracted from the large model factory into a focused partial file.

Verification:

- The final focused run passed 60 cases: all 59 new tests plus one existing overlapping mapped
  collection regression. Sixteen actual generated SQLite cases compare saved state/outcomes,
  null indexers, original versions, hidden/missing rows, Stage/shared Save, pending-state isolation,
  cancellation and pre-save failure restoration. SQLite and SQL Server output compile in all three
  layouts; SQL Server compile cases do not open a connection. Compatibility/closed surfaces,
  strict opt-outs, quoted metadata, typed permissions and stale protection bindings are covered.
- The full 107-project Release build passed with zero warnings/errors. All 79 example applications
  passed, including the field/shadow/indexer designer's conditional and shared-save paths.
- All 79 configurations regenerated with target-reference compilation; the final no-write freshness
  check and approved Ledger API baseline passed. There are 312 managed outputs: four added files,
  all 308 prior hashes unchanged and none removed. All 58 fixture-only dependency inventories exclude
  EF, persistence models, designers and generation tooling, including the new example.
- The 45 scoped implementation/example files passed whitespace/internal-name checks; the working
  diff has no whitespace errors and all 46 wiki-tooling checks passed.
- The complete five-suite run passed 3,949 tests with 32 unconfigured live SQL Server skips and no
  failures: 3,423 tool tests and another 526 runtime/generator tests. All 59 new cases passed in that
  full run. Live SQL Server behavior is not claimed by the compile-only new provider checks.
- The final Markdown/link audit and local wiki export are part of this verification; no remote
  publication or external application source changes were performed.

## Delivered named mapped batch setters slice (2026-10-21)

F09/F15.4 extend explicit mapped scalar assignments to bounded root-row PatchMany without adding
field-name inputs, setters or EF query construction to services. Presets/default output are unchanged.

- The item builder supports exact typed model-name targets with constants, current-row or item
  values and item-only conditions. Shadow, field-backed/field-only and indexer targets use EF entry
  CurrentValue under the existing batch snapshots. Current scalar reads use the same exact typed
  lowering as root Patch, in declaration order; field access follows configured EF access mode.
- Existing finite input bounds, one filtered target query, visibility, duplicate/missing/dirty target
  guards, caller-original tokens and explicit Save/Stage retain their behavior. A later stale item,
  mapping exception or provider save failure restores earlier assignments. Shared Save remains a
  shared unit-of-work save, not an entity-only save, bulk SQL statement or round-trip guarantee.
- Typed ProtectWrite/AllowWrite remain command-owned design-time permissions. Original checks and
  deliberate caller-managed advancement remain distinct; no token advancement is inferred.
  Named keys/FKs/unique identity, generated/read-only/init targets, owned/shared/inherited entities,
  CreateMany, child/graph and native/atomic targets remain excluded. DeleteMany cannot gain setters.
- Fixtures independently require complete exact public-data property/write-state maps for non-public
  targets. Translated scalar slots never reuse generated context.Entry code. Saved scalar DTOs work
  without automatic reads; strict unsupported operations or typed scripts, pending/reset guards,
  checkpoints, Stage/shared Save and original versions retain their semantics even with empty input.
  There is no inferred EF tracker, SQL constraint, generated rowversion or provider/race equivalence.
- Optional mappedBatchUpdateTargets receipts describe exact bindings and the independent boundaries.
  [MappedBatchSetters](../../samples/ConfigurationExamples/MappedBatchSetters/README.md) links the
  one-table ReviewEntriesRepositoryDesigner to four exact outputs and an EF/model-free port consumer.
  The [mapped batch setter guide](../implemented/writes/MAPPED-BATCH-SETTERS.md) explains configuration,
  entry access modes, failure restoration, lifecycle guards and fixture consent. Root and batch
  setter validation/current-read lowering share focused helpers rather than duplicate implementations.

Verification:

- All 49 new cases passed: 30 generation cases and 19 execution cases. Sixteen execute actual
  generated SQLite code; three cover fixture-only behavior. Cases include mixed/member/constant/null
  assignments, declaration order without behavioral getter reads, original tokens, stale later items,
  hidden/missing rows, disabled automatic change detection, Stage/shared Save, mapping/provider
  failure restoration, dirty targets, cancellation, checkpoints and baseline guards. SQLite and
  SQL Server outputs compile in all three layouts; SQL Server compile cases open no connection.
- The complete 108-project Release build passed with zero warnings/errors, and all 80 example
  applications passed. All 80 generation configurations passed .NET 8 target-reference compilation,
  the final no-write freshness check and the approved Ledger API baseline.
- There are 316 managed generated files: four additions, all 312 prior hashes unchanged and none
  removed. All 59 fixture-only dependency inventories exclude EF, persistence models, designers
  and generation tooling, including the new example. All 46 wiki-tooling checks passed.
- The complete five-suite run passed 3,998 tests with 32 unconfigured live SQL Server skips and no
  failures: 3,472 tool tests and another 526 runtime/generator tests. All 49 new mapped batch cases
  passed in that full run and in an independent focused run. Compile-only provider checks do not
  claim live SQL Server behavior.
- Scoped implementation/example/documentation files and the working diff passed whitespace checks;
  scoped files also contain no internal application/company names. The final Markdown audit and
  local wiki export are included in verification; no remote publication or target application
  source changes were performed.

## Delivered mapped reconciliation setters slice (2026-10-22)

F09/F15.4 extend explicit named scalar targets to parent Patch and matched leaf-child/explicit-link
updates under existing bounded reconciliation. No service-side EF DSL, new global switch or preset
change is introduced.

- Child SetProperty/SetPropertyWhen support exact typed constants, current-child and item values.
  Shadow, backing-field/field-only and indexer assignments use Entry(child), while parent values
  use parent entry state. Declaration order, nullable values and configured EF access modes remain
  explicit. Dynamic/foreign/mistyped reads and navigation-dependent mapped value recipes fail.
- Child-scoped AllowWrite<T>(name) validates exact child metadata separately from root grants.
  Entity ProtectWrite still applies. Identically named parent/child tokens cannot share permission
  or fixture slots. Registered adapters expand in child scalar values, conditions and expected versions.
- Existing original-token guards, finite limits, owner/visibility filters, dirty-child checks,
  Keep/Reject/Delete, explicit Stage/shared Save and current/original/member snapshot restoration
  retain their semantics. No stale-input retry, one-round-trip claim or implicit aggregate loading
  is added. Unrelated pending work is preserved on failure and shares a successful Save.
- Primary/alternate/matching keys, ownership/FKs, unique identity, generated/read-only/init targets,
  owned/shared/inherited entities and broader graphs remain excluded from named assignment.
  Link endpoints/references remain immutable. Named matched-row setters do not become Add recipes;
  creation, implicit joins, skip navigations and native/atomic targets remain separate boundaries.
- Fixtures separately require complete participant property/write-state bindings and command
  SimulateCollectionWrites. They translate original recipes to typed slots, not production EF entry
  code. Saved graph read consent stays independent; missing capability still requires a typed script
  or strict diagnostic even when empty/skipped. Provider constraints, tracker state, rowversions,
  isolation and races are not inferred.
- Optional mappedReconciliationUpdateTargets receipts identify navigation, leaf/link kind, limits,
  exact target types and scoped capability boundaries. The focused
  [MappedCollectionSetters designer](../../samples/ConfigurationExamples/MappedCollectionSetters/README.md)
  links its two-table recipe to four exact outputs and an EF/model-free port consumer. The
  [mapped reconciliation guide](../implemented/writes/MAPPED-RECONCILIATION-SETTERS.md) documents
  configuration, permissions, entry access modes, rollback and deliberate limits.

Verification:

- All 66 focused cases passed: 38 generation/guard cases and 28 execution cases. Twenty-six run
  actual generated SQLite operations; two cover fixture-only checkpoint/override behavior. Coverage
  includes separate parent/child grants, literal/type/identity guards, nullable/binary original tokens,
  conditional/current/constant values, adapters, hidden rows, shared links, owner mismatch, finite
  input, Keep/Delete/Reject, Stage/shared Save, disabled detection, mapper/provider failures,
  dirty-state restoration, cancellation, checkpoints and baselines.
- SQLite and SQL Server output compile in all three layouts; compile-only SQL Server checks open
  no connection. The 109-project Release build passed without warnings/errors. All 81 examples,
  generation target-reference compilation, final no-write freshness and the Ledger API baseline passed.
- There are 320 managed generated artifacts: the new example adds four; prior configurations
  regenerated without changed files. All 60 fixture-only dependency inventories exclude EF, models,
  designers and generation tooling, including the new example.
- The complete five-suite run passed 4,064 tests with 32 unconfigured live SQL Server skips and no
  failures: 3,538 tool tests and another 526 runtime/generator tests. All 66 new cases passed in
  that full run and an independent focused run. No live SQL Server behavior is claimed.
- All 56 scoped implementation/example/documentation files passed whitespace/internal-name checks,
  the working diff has no whitespace errors, all 46 wiki-tooling checks passed, and the final
  Markdown/link audit and local wiki export were verified. No remote publication or external
  application source changes were performed.

## Deliberate remaining boundaries

Explicit [fixture mapped scalar bindings](../implemented/testing/INMEMORY-MAPPED-READS.md) supply
shadow/field/indexer/CLR values from public data for getter-free named reads and separately enabled
scalar filters/declared graphs. They do not simulate tracked property state, original concurrency
tokens, token advancement, shadow relationship identity, native returned buffers or saved command
results. Simple defaults and existing production contracts/implementations remain unchanged.

Separate [mapped relationship-key permission](../implemented/testing/INMEMORY-MAPPED-KEYS.md) now
admits exact bound principal/FK values for one detached read edge, including explicit CLR joins.
This is not fixture dictionary identity, write/graph persistence, original-token simulation or an
implicit/shared join bridge. Related scalar projections still need their own supported read maps.

Separate [mapped scalar write-state consent](../implemented/testing/INMEMORY-MAPPED-WRITES.md) now
permits complete exact non-identity state, original-version checks and deliberately configured
generated values in public-data fixtures. Ordinary primary/FK/unique identity, graph reconciliation,
new private/shadow setter targets, native returned buffers and EF/provider state remain separate.

Separate [mapped scalar returned-value simulation](../implemented/testing/INMEMORY-MAPPED-RETURNING.md)
now reuses complete permitted write state for post-update/pre-delete scalar receipts. Explicit
capture and immediate consent remain required; this simulates fixture values, not native buffers,
provider materialization, inferred rowversions or SQL/graph guarantees.

Separate [mapped scalar collection-state consent](../implemented/testing/INMEMORY-MAPPED-COLLECTIONS.md)
now reuses those complete maps within existing explicitly bounded leaf-child/link reconciliation.
Original keys/ownership, shape guards, independent model filters and lossless shared Save remain;
this is not arbitrary entity-graph persistence, relationship-key simulation or provider equivalence.

The latest [child-store reconciliation slice](../implemented/testing/INMEMORY-COLLECTIONS.md), including
[composite matching keys](../implemented/writes/COMPOSITE-COLLECTION-KEYS.md), closes the explicitly designed
bounded leaf-child write portion of F15.4/IM08-IM09. [ReturnsSaved](../implemented/writes/SAVED-COMMAND-RESULTS.md) adds explicit
fresh result DTOs through the declared ordinary navigation-read subset, including
[bounded patch batches](../implemented/writes/SAVED-BATCH-RESULTS.md), [required explicit many-to-many reads](../implemented/testing/INMEMORY-MANY-TO-MANY.md)
and [guarded nullable-link reads](../implemented/testing/INMEMORY-OPTIONAL-LINKS.md).
Bounded [CreateMany](../implemented/writes/CREATE-MANY-COMMANDS.md) adds scalar creation and input-ordered saved DTOs
without a matching key or native bulk-insert claim.
Bounded [explicit association-row writes](../implemented/writes/LINK-RECONCILIATION.md) additionally reconcile two-FK join
references/payload through an owner-scoped join collection without persisting endpoint graphs.
Compatible owner/target FK overlaps now have explicit [complete-key guards](../implemented/writes/SHARED-REFERENCE-COLUMNS.md).
Required disjoint additional FKs now have explicit [named reference roles](../implemented/writes/ADDITIONAL-LINK-REFERENCES.md)
and complete Add-only maps with immutable matched references. [Explicit nullable additional roles](../implemented/writes/OPTIONAL-ADDITIONAL-REFERENCES.md)
add complete all-or-none creation with immutable matched presence/values.
[Required shared additional columns](../implemented/writes/SHARED-ADDITIONAL-REFERENCES.md) separately reuse
the explicit shared-column opt-in, checking new owner-shared values before binding, even outside
the target key. Additional inputs on matched rows stay ignored and references remain immutable.
[Owner-shared optional references](../implemented/writes/OWNER-SHARED-OPTIONAL-REFERENCES.md) additionally
keep required owner columns complete while nullable independent components define absence/presence.
New partial independent keys and wrong-owner values are rejected before binding; matched and
preexisting reference values are not normalized.
Broader owned/nested graph writes, direct skip-navigation mutation, optional-target/unaccounted/non-owner-optional-overlap write
shapes, implicit/shared joins, unsafe nullable-target expressions and explicit optional-target loading remain
extensions, not inferred aggregate behavior; ordinary graph `Returns` remains scripted.
Optional [null-preserving fixture reads](../implemented/testing/INMEMORY-NULL-TARGETS.md) now retain per-link missing targets
for explicitly null-safe scalar/DTO pipelines, without simulating general SQL propagation.
Explicit [principal-component comparison](../implemented/testing/INMEMORY-RELATIONSHIP-COMPARERS.md) now covers declared
fixture-read relationship matching and uniqueness without changing queries, store identity or writes.
Explicit [flat DTO helper translations](../implemented/testing/INMEMORY-PROJECTION-TRANSLATIONS.md) additionally cover
approved scalar-input result recipes without inferring arbitrary mapper/graph or SQL behavior.
Separately enabled [nested DTO helper recipes](../implemented/testing/INMEMORY-NESTED-PROJECTIONS.md)
add bounded independent result-data DTOs and materialized arrays/lists through per-registration
consent. Scalar inputs, separate command permission and unchanged production translation remain;
entity-bearing, recursive, captured/deferred and arbitrary mapper graphs retain diagnostics.
Optional [final-model scalar filters](../implemented/testing/INMEMORY-MODEL-FILTERS.md) additionally cover opted-in row-only
read roots without duplicating filters in the designer. Separately opted-in
[ordinary filtered graphs](../implemented/testing/INMEMORY-FILTERED-NAVIGATION.md) additionally cover collections and
locally guarded optional references before loading. [Explicit context scalar bindings](../implemented/testing/INMEMORY-MODEL-FILTER-CONTEXTS.md)
now use typed partition keys without evaluating captured context values. Unbound context-dependent policies, required filtered
ordinary references, implicit/shared joins and active AutoInclude remain explicit override/provider boundaries.
Optional [named-query loading suppression](../implemented/queries/EXPLICIT-LOADING-POLICIES.md) now disables user-configured
model loading in production while retaining explicit paths/profiles, root filters and visibility.
Owned loading and command policies remain separate; automatic fixture graphs are not inferred.
Optional [per-command scalar filters](../implemented/testing/INMEMORY-MODEL-WRITE-FILTERS.md) additionally cover eligible
scalar writes/batches/immediate counts and fresh saved DTOs without filtering insertion/shared Save.
Separately opted-in [filtered saved DTO graphs](../implemented/testing/INMEMORY-SAVED-FILTERS.md) additionally cover ordinary
collections and guarded optional references after scalar writes/shared Save. Broader graph mutations,
required filtered ordinary references, implicit/shared joins and additional provider policies retain explicit boundaries.
Separately opted-in [filtered explicit CLR many-to-many reads](../implemented/testing/INMEMORY-FILTERED-LINKS.md)
now cover endpoint/join scalar filters and fresh saved DTOs under independent graph/command permissions.
Required hidden targets omit links; optional targets keep their explicit null policy. Unfiltered
principal keys still validate actual missing/duplicate fixture rows, without inferring SQL constraints
or graph persistence.
Separately opted-in [model-filtered child/link reconciliation](../implemented/testing/INMEMORY-COLLECTION-FILTERS.md)
now selects owner-scoped portable candidates before bounds/matching/missing policies. Independent
write permission does not filter insertions/shared Save, relax all-owner dirty checks or grant
read/saved-result permission. SQL constraints, endpoint authorization and broader graph persistence
remain separate.
Separately opted-in [scalar filtered atomic fixtures](../implemented/testing/INMEMORY-ATOMIC-FILTERS.md)
now preserve unfiltered conflicts/insertion, filtered updates and model-only updated result reads.
Filter-changing result failure remains distinct from native Update/Delete values and tracked ReturnsSaved.
Separately opted-in [model-filtered native scalar results](../implemented/testing/INMEMORY-NATIVE-FILTERS.md) now reuse
saved selection without fresh result filtering. Unmapped fixture native values, returned graphs, context-based
unbound filters, additional dialects and relational guarantees remain separate.

Production [explicit native shadow-value buffers](../implemented/writes/RETURNING-SHADOW-VALUES.md)
add ordinary mapped scalar results without tracker entries. They do not enable fixture shadow state,
shadow keys/setter targets, backing-field/indexer mappings or returned navigation graphs.

The separate production [mapped-value capture permission](../implemented/writes/RETURNING-MAPPED-VALUES.md)
now admits supported writable field/indexer materialization and getter-free scalar results.
Shadow-only capture remains narrower. Ordinary CLR keys/setter targets, constructor/table guards,
independent fixture-state/override consent and broader graph/provider restrictions remain explicit boundaries.

Separate [native atomic scalar capture](../implemented/writes/ATOMIC-SCALAR-CAPTURE.md) additionally
supports explicit insert/upsert result buffers and filtered no-tracking updated scalar reads.
Atomic writer capabilities, ordinary keys/setter targets, borrowed-transaction rollback and complete
fixture-state/factory permissions remain distinct; graph, identity and provider guarantees are not inferred.

Separate [fresh atomic result queries](../implemented/writes/ATOMIC-SAVED-RESULTS.md) now project saved
DTO graphs by complete saved identity inside insert/upsert transactions. Fixture prepared overlays,
saved-only related rows and independent read/filter permissions do not broaden graph writes,
native returning graphs, identities or provider guarantees.

Separate [bounded fresh native-update results](../implemented/writes/NATIVE-SAVED-RESULTS.md) now
correlate directly updated complete keys before owned commit or borrowed savepoint release.
The abort-on-excess bound, independent snapshot/read/filter permissions and exact cardinality do
not imply native graph RETURNING, fresh deleted roots, graph mutation or provider equivalence.

Production [typed mapped-property bindings](../implemented/repositories/MAPPED-PROPERTY-BINDINGS.md)
add explicit named/shadow accessors and original-token checks without automatic shadow-index
inference, token advancement or runtime service property names. Fixture shadow/non-public tokens
need their separate complete state consent or overrides; native setter/key restrictions and broader
provider behavior remain separate boundaries.

Separate [named mapped command setters](../implemented/writes/MAPPED-COMMAND-SETTERS.md) now assign
exact non-identity scalar state during scalar tracked Patch/ordinary Update. Named protection and
explicit command permissions do not bypass key/FK/unique/generated guards. Original checks and
caller-managed advancement remain distinct; complete fixture state is independently permitted.
Separate [named mapped PatchMany item setters](../implemented/writes/MAPPED-BATCH-SETTERS.md) extend
the scalar policy to bounded root-row batches with unchanged snapshots, original tokens, limits,
shared Save/Stage and independent fixture consent. Separate
[mapped reconciliation setters](../implemented/writes/MAPPED-RECONCILIATION-SETTERS.md) now support
parent and matched leaf-child/link scalar targets, with independent child grants and participant
state. Native-returning, atomic, broader graph setters, matching/identity/generated targets and
provider guarantees retain their existing boundaries.

The latest public-proxy, navigation, tracked-write, bounded-batch, generated-value, mapped-navigation and mapped-write slices extend earlier single/multi-source receipts; their verification
counts and unsupported-shape notes above describe those historical deliveries.

The fixture-key slice closes the configurable-comparer portion of F15.2/IM04. It adds no production
comparison/collation semantics or claim of universal custom-comparer correctness.

Optional [constant model defaults and scalar sentinels](../implemented/testing/INMEMORY-MODEL-DEFAULTS.md) additionally
reuse eligible insert-only constants with explicit factory precedence, without inferring SQL,
identity/graph/provider behavior or applying defaults to seeds/updates/shared Save.

The F01-F14 roadmap capabilities are implemented with their optional/provider boundaries, including
CQRS-selector splitting. F15's bounded scripted/store/read/navigation/scalar-write/batch/value/map/native,
creation, child and explicit-link reconciliation slices, plus reusable contract-suite/relational fixtures,
are delivered.
Optional scalar-call fixture translations extend named reads and separately opted-in command
selection/value/result expressions without inferring additional provider or store/value-adapter behavior.
Explicit scalar equality additionally covers tracked non-identity state/versions with unchanged
lossless adapters, query/key/relationship semantics and separate provider/conversion boundaries.
Broader graph shapes, nonportable result operations and additional value/provider policies
remain extensions. Arbitrary entity behavior/identity is not proxy-adapted; not every possible extension
is complete. Compatibility selectors remain combined/EF-facing; strict EF-free named operations use
closed mode. Business adapters, authorization, distributed durability, additional dialects and
cross-database federation are not silently added. Advanced infrastructure stays optional and designer-configured.
