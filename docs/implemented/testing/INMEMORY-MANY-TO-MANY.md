# Optional many-to-many fixture reads

`HasInMemoryNavigation` can load a many-to-many navigation through an explicitly selected CLR
join entity. The EF model supplies both foreign keys and their principal keys. The designer
selects the endpoint/join stores and the navigation edges; the consumer still calls the same
named operation as production. No second service-side query DSL or seeded graph is needed.

Start with [TaggedArticleRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryManyToMany/Configuration/TaggedArticleRepositoryDesigner.cs)
and its [exact output map](../../../samples/ConfigurationExamples/MemoryManyToMany/README.md).
This extends [navigation reads](INMEMORY-NAVIGATION.md), not graph persistence.

Explicit per-edge [mapped property keys](INMEMORY-MAPPED-KEYS.md) can supply typed shadow/field/indexer
components for both principals and the explicit CLR join. The ordinary defaults below remain unchanged;
this does not add implicit/shared join support or write state.

## Opt in using existing declarations

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Generated")
    .UseScriptedTestDoubles("Tests.Generated")
    .UseInMemoryTestStores().UseInMemoryTestQueries()
    .RequirePortableTestQueries());

modelBuilder.Entity<TaggedArticle>(entity =>
{
    entity.WriteEnabled = false;
    entity.HasInMemoryStore<ArticleRow, (int, int)>(
            row => new ValueTuple<int, int>(row.TenantId, row.Id),
            row => new(row.TenantId, row.Id, row.Title))
        .MapInMemoryEntity<ArticleRow>(row => new TaggedArticle
            { TenantId = row.TenantId, Id = row.Id, Title = row.Title });
    entity.HasInMemoryNavigation(row => row.Tags);
    entity.HasQuery("Cards").OrderBy(row => row.TenantId).ThenBy(row => row.Id)
        .Select(row => new ArticleCard(row.TenantId, row.Id, row.Title,
            row.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList()))
        .List();
});
```

The example also declares a Tag store/map and a Link store/map. Its EF configuration uses
`HasMany(...).WithMany(...).UsingEntity<TaggedArticleLink>(...)` with required join foreign keys.
There is no additional generator option, duplicated relationship key expression or inferred join
table. Entity-shaped public stores can be used instead of public data maps; their navigation
properties retain the ordinary readable/writable List-compatible loading requirements.

| Participating table | Public fixture row | Keys used for this edge |
| --- | --- | --- |
| TaggedArticle | ArticleRow | Principal TenantId + Id |
| ArticleTag | TagRow | Principal Id |
| TaggedArticleLink | ArticleTagRow | Foreign TenantId + ArticleId; foreign TagId |

Relationship keys need not equal fixture dictionary keys. Alternate/composite principal keys
retain EF component order, and the two sides may have different key arities. Every referenced
key must be publicly readable or explicitly scalar-mapped. Internal/read-only persistence types
can use [plain public-data read rows](INMEMORY-PROXY-NAVIGATION.md) at all three tables; constructors,
entity methods and navigation setters are not invoked.

All declarations are optional. Removing a navigation/store/map or disabling automatic fixture
reads changes only testing coverage/output—not production repositories, contracts, API shapes or
cache query fingerprints. Simple generation emits no testing infrastructure.

## One detached snapshot, explicit edges

An operation captures both endpoints and the join store together under one fixture-partition
lock. Copying finishes before typed loading/query execution outside the lock. Unused tables are
not captured, and store-only joins do not need repository query methods. Each endpoint is mapped
once and can be shared by several related lists within that detached invocation.

Generated code indexes both principal-key sets and walks the join rows. It creates a separate
List for each source, including empty sources. List order follows the join snapshot—not a SQL
ordering guarantee. Put an explicit related OrderBy in the shared projection when order matters.
Mutating returned DTO collections never changes fixture stores, baselines or later calls.

Only accessed/loaded navigation edges are rebuilt. Declaring an inverse does not populate it for
every operation; an accessed inverse requires its own `HasInMemoryNavigation` declaration.
Unfiltered Include/ThenInclude and load profiles can select edges. Pages and cold streams retain
one detached participating-table snapshot for the invocation/enumeration. Existing scripts still
take precedence over the generated backend.

RepoGen visibility remains a **root** policy. Neither endpoint nor join-repository visibility
policies are automatically injected into navigation loading. Put intended child filters in the
shared projection/predicate, or use an explicit From root when join-row payload/policies are part
of the query. Required navigation-visibility review still applies when enabled. EF global filters,
AutoInclude and custom converted/provider key behavior retain their explicit override boundaries.
Opted-in [scalar model filters](INMEMORY-FILTERED-LINKS.md) now cover explicit endpoint/join datasets;
each filtered participant needs graph permission. Required filtered targets omit links, not owners,
while actual missing required principals still fail fixture validation.
Optional [principal-component comparison](INMEMORY-RELATIONSHIP-COMPARERS.md) explicitly configures
source/target matching and complete-pair uniqueness for compatible CLR keys; query/store/write
equality and nullable-target policies are unchanged.

## Fixture validity is explicit, not a SQL simulation

- Principal relationship keys and link pairs must be unique under ordinary CLR/structural equality.
  This includes array components; it does not apply database collation or fixture-key comparers.
- Null in any key component never matches. Required join references must resolve to participating
  principals; an invalid required reference fails instead of inventing a row or silently dropping it.
- All captured relationship rows are validated before assigning this edge, even when a later root
  predicate would hide a bad owner. Duplicate pairs fail even if a surrogate join ID is unique.
- Cancellation is checked throughout preparation/assignment, including the last callback.
  A failed detached load never mutates fixture data; it is not a database constraint/transaction test.

Automatic lowering requires an ordinary explicit CLR join entity. Required join foreign keys
retain the original subset above. [Nullable join links](INMEMORY-OPTIONAL-LINKS.md) additionally
support optional source FKs and explicitly non-null-filtered optional targets; unguarded target
projection/count/loading remains scripted because SQL can retain null elements. Implicit shared
property-bag joins, unpermitted/unbound shadow keys, owned/inherited/shared graphs, missing scalar maps, provider key
conversions/comparers and filtered includes also require an override or real-provider fixture.
Explicit From joins remain available where their independent portable subset fits.

An explicit per-navigation [null-preserving policy](INMEMORY-NULL-TARGETS.md) now additionally retains
missing optional targets for null-safe counts/presence/DTO projections; it does not authorize unsafe
dereferences, whole-entity results or explicit loading. The guarded-only default remains unchanged.

Unsupported operations report deterministic RequiresOverride reasons even for empty stores.
`RequirePortableTestQueries()` rejects them with RGD3302 unless a typed
`RequireTestOverride(reason)` acknowledgement is supplied. Existing RGD3304 member validation
still applies. Manifest `navigationSnapshot.relationships[].join` records the join store, both
foreign-key expressions, requiredness, pair validation, ordering and lack of policy injection.

## Fresh command results reuse this read subset

Suitable [ReturnsSaved](../writes/SAVED-COMMAND-RESULTS.md) and
[bounded saved batch results](../writes/SAVED-BATCH-RESULTS.md) may project these explicitly declared
relationships after the shared save. Their `savedResultQuery.joinRelationships` receipt describes
the join reads. Invalid result graphs fail with RepositorySavedResultException after saving;
they do not undo persisted scalar writes. This does not allow navigation-dependent mutation
setters, many-to-many reconciliation, graph creation/deletion or inferred aggregate behavior.

Shared tests compare actual generated SQLite operations with both entity-shaped and mapped
fixtures: nested DTOs, membership, inverse projections, unfiltered chains, root/related visibility,
composite alternate keys, pages, and single/batch saved results. These bounded cases do not establish
universal provider translation, arbitrary SQL null propagation, transaction or concurrency equivalence. Use the
[real-provider contract fixture](REPOSITORY-CONTRACT-FIXTURES.md) for those guarantees.

## Explicit association-row writes are separate

[ReconcileLinks](../writes/LINK-RECONCILIATION.md) now writes suitable ordinary two-FK join rows through their
owner's explicit one-to-many join collection. It shares the bounded production/fixture reconciliation
rules and can use these reads for ReturnsSaved. It does not mutate a skip-navigation graph or infer
target creation/deletion, optional links, overlapping FKs or nested aggregate behavior.
