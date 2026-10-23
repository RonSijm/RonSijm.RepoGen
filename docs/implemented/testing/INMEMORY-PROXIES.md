# Explicit public data mappings for portable fixture reads

`MapInMemoryEntity<TData>` extends [portable in-memory reads](INMEMORY-QUERIES.md) without exposing
private/internal persistence entities in the testing consumer. A public arrangement shape can
live in a contracts/test-data assembly, while the original RepositoryDesigner continues to own
production predicates, visibility, sorting, projections and named operations.

## Three different shapes

- The **EF entity** is the persistence model. It can be internal.
- The **public fixture row** is consumer-owned arrangement data, with explicit key/copy adapters.
- The **public result DTO** is the application's query result. It need not resemble either row.

The generator does not guess or reverse an entity-to-DTO projection to produce fixture data.
Declare the relationship deliberately:

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Generated")
    .UseGeneratedNamespace("Persistence.Generated")
    .UseScriptedTestDoubles("Tests.Generated")
    .UseInMemoryTestStores()
    .UseInMemoryTestQueries());

modelBuilder.Entity<DirectoryRecord>(entity =>
{
    entity.WriteEnabled = false;
    entity.HasInMemoryStore<DirectorySeed, int>(row => row.Key,
            row => new DirectorySeed(row.Key, row.Label, row.Credits, row.Archived))
        .MapInMemoryEntity<DirectorySeed>(row => new DirectoryRecord
        {
            Id = row.Key,
            Name = row.Label,
            Points = row.Credits * 10,
            Disabled = row.Archived
        });
    entity.HasVisibilityPolicy("Enabled", row => !row.Disabled);
    entity.HasProjection(row => new DirectoryCard(row.Id, row.Name, row.Points));
    entity.HasQuery("Find").Where<int>((row, id) => row.Id == id)
        .ProjectTo<DirectoryCard>().SingleOrDefault();
});
```

The initializer is a **declaration**, not a factory. Generated test code creates a plain internal
read row containing only those scalar assignments. It never constructs DirectoryRecord, calls
its setters/constructors or references its type in executable code. The original entity name may
appear in diagnostic strings/store identities; it is not a model-assembly dependency.
Designer lambdas are inspected/rendered, not compiled or invoked during generation. Registered
type-adapter markers in assignment values are expanded by the normal designer pipeline.

## Supported mapping contract

Use a matching preceding `HasInMemoryStore<TData, TKey>` with a distinct public row shape. Keys,
detached copies and nested mutable ownership still follow [the store rules](INMEMORY-FIXTURES.md).
Replacing/removing the store clears its mapping; `WithoutInMemoryEntityMapping()` removes just
read adaptation and its dependent write/value adapters. The scalar map is emitted with automatic reads,
or when separately enabled [mapped writes](INMEMORY-PROXY-WRITES.md) require its scalar slots.

The mapping must contain a parameterless entity initializer with at least one **direct EF scalar
property/field assignment**. Scalar CLR types must be public/closed and usable independently of
the persistence assembly. Private enums/value types need an application-owned override rather
than leaking through the read row. Navigation, ignored/computed members, factory calls, constructor
arguments, nested member bindings and unsupported/ambient assignment expressions produce RGD3203.
The entity initializer's constructor, default property initializers and any unassigned members
are deliberately not executed or copied.
Assignment values cannot construct/carry mapped persistence entities either; reserving the outer
initializer does not permit a hidden entity factory inside a scalar value. Reserved read-row/member
name collisions also report RGD3203. Existing entity-selection rules still apply: this option does
not make nested/private or generic EF entity types generatable.

Separately declared `HasInMemoryNavigation` edges can now load plain read-row relationship slots
when their participating stores also have suitable scalar maps. Do not assign navigations inside
the scalar initializer. See [mapped public-shape navigation](INMEMORY-PROXY-NAVIGATION.md).

Assignment values use the conservative portable expression subset. They and key/copy adapters
must be pure and self-contained. .NET value transformations are not a promise of SQL collation,
conversion or provider equivalence. A map describes values after they become persistence scalars;
it is not an EF value-converter simulation or an inferred write adapter.

## Coverage is checked per operation

Every scalar actually referenced by predicates, active visibility, active default sorting, dynamic
sort whitelists, named projections, groups, dictionaries/aggregates and cursors must be assigned.
Omitting a referenced member makes that operation RequiresOverride; it never fabricates a default
even for an empty store. Explicit visibility bypasses and default-sort opt-outs do not require
their unused members. `RequirePortableTestQueries()` rejects unacknowledged unsupported reads
with RGD3302; `RequireTestOverride(reason)` retains the typed script and documents the boundary.

Two or more mapped roots also work in explicitly designed From/Set sources. Their **public fixture
rows** are captured together, then mapped and filtered before joins/subqueries/result composition,
outside the fixture lock. Scalar set operations are supported after projecting keys/values.
Anonymous intermediate carriers support group/left joins, including null checks on unmatched rows.
All ordinary snapshot, paging, partition, cold-stream, cancellation and script-reset rules remain
those of the read backend.

Whole-entity identity/behavior is not scalar adaptation: entity equality/set operations, identity
projections, entity constructors/methods, model casts/type tests and explicitly rendered generic
model types require overrides. [Explicit detached navigation loading](INMEMORY-PROXY-NAVIGATION.md)
is supported; tracker fixup, EF inheritance/AutoInclude/model filters,
provider methods and relational guarantees retain the existing boundaries. Use the real-provider
[fixture harness](REPOSITORY-CONTRACT-FIXTURES.md) to verify those behaviors.

The manifest adds a store `readMapping` receipt listing the generated read row and selected scalar
members, `constructsPersistenceEntity: false` and `writes: NotAdapted`. Declared relationship slots
add `navigationSlots` and operation-specific participating-store/key receipts. Per-operation coverage still
reports Generated/RequiresOverride. This is auditable test infrastructure, not a second service DSL.

## Defaults and production behavior

All testing switches remain **off by default**. No new global mapping setting is required.

An independent [MapInMemoryProperty](INMEMORY-MAPPED-READS.md) declaration can add exact shadow,
field-backed, field-only, indexer or ordinary CLR scalar values from the same public store. Named
reads and separately opted-in filters reuse these getter-free values. An initializer and a named
binding cannot both supply the same scalar. This does not supply tracked property state, original
token checks or shadow relationship identity.

- With only a public store, reads stay scripted; no mapping is inferred from member names.
- Removing the map or `WithoutInMemoryEntityMapping()` retains arrangement storage and scripts.
- `UseInMemoryTestQueries(false)` disables automatic reads; a separate write adapter can retain scalar helpers.
- `GenerateTestDouble(false)` excludes that entity from test output/portability checks.
- Production queries/contracts/API snapshots/cache query fingerprints do not change.
- Writes, Stage/Save, generated values and rollback are **not** enabled by this read mapping.

[Explicit public fixture writes](INMEMORY-PROXY-WRITES.md) separately support scalar Create/Patch/Delete,
bounded batches, generated-value factories and shared Stage/Save/rollback through `MapInMemoryWrites`.
See [MemoryProxyWrites](../../../samples/ConfigurationExamples/MemoryProxyWrites/README.md). Read mapping alone
still does not infer reverse transformations or write behavior.

See [MemoryProxies](../../../samples/ConfigurationExamples/MemoryProxies/README.md) for an independently
runnable EF/model-free consumer and the exact designer → generated-file mapping. Tooling tests
also compile the output with no reference to the assembly containing the original model, exercise
two mapped private sources, and compare shared named operations with actual generated SQLite
repositories. Such checks establish those cases, not universal database equivalence.

[MemoryProxyNavigation](../../../samples/ConfigurationExamples/MemoryProxyNavigation/README.md) focuses on
internal/read-only model navigations with public immutable seeds, nested DTOs, composite keys,
explicit child/root policies, load profiles, counted pages and cold mapped streams.
