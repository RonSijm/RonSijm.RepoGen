# Optional fixture relationship-key comparison

`HasInMemoryRelationshipComparer(property, comparerFactory)` configures one primary/alternate
**principal-key component** for detached fixture relationship reads. It is independent of a
store's key comparer and of tracked scalar equality. Nothing is inferred from property names,
an EF comparer or SQL collation, and ordinary structural comparison remains the default.

Start with [ComparedDirectoryRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryRelationshipComparers/Configuration/ComparedDirectoryRepositoryDesigner.cs)
and its [exact designer → output map](../../../samples/ConfigurationExamples/MemoryRelationshipComparers/README.md).

## Configure the principal once

```csharp
var folders = modelBuilder.Entity<StoredFolder>();
folders.HasInMemoryStore<FolderSeed, int>(row => row.Id,
        row => new(row.Id, row.Region, row.Code, row.Name))
    .MapInMemoryEntity<FolderSeed>(row => new StoredFolder {
        Id = row.Id, Region = row.Region, Code = row.Code, Name = row.Name });
folders.HasInMemoryRelationshipComparer(row => row.Code,
    () => StringComparer.OrdinalIgnoreCase);
folders.HasInMemoryNavigation(row => row.Entries);
modelBuilder.Entity<StoredFolderEntry>().HasInMemoryNavigation(row => row.Folder);
```

The EF relationship uses the complete `(Region, Code)` alternate key. Only Code gets the explicit
policy; Region retains structural equality. Both Entries and Folder use that principal policy,
regardless of traversal direction. The dependent entity does not repeat the declaration on its FK.
Each participating store and accessed navigation must still be explicitly selected.

The policy requires a preceding HasInMemoryStore and automatic fixture queries. It does not enable
either backend, declare a navigation, or add an application-side query/comparer API. It works with
public entity-shaped stores and explicit public scalar maps for internal/read-only models.
WithoutInMemoryRelationshipComparer removes one component. Replacing/removing its store removes
its policies; disabling automatic queries emits no comparison helper or receipt.

## Scope and invariants

| Behavior | Comparison |
| --- | --- |
| Reference/collection FK-to-principal matching | Declared principal component; structural fallback for other components |
| Explicit CLR many-to-many source and target keys | Independently assembled from each principal's declarations |
| Principal uniqueness and complete join-pair uniqueness | The same relationship comparison, not the stores' primary-key identity |
| Null/incomplete FK | Never joins, even with an explicit comparer |
| Store Find/Seed/Remove/duplicates | Unchanged; separate fixture-key comparer |
| Query predicates, membership, grouping, dictionaries and sorting | Unchanged designer expressions and their supported CLR semantics |
| Tracked writes, versions, matching keys and copy/write-map validation | Unchanged; relationship policy cannot acknowledge those write semantics |
| Production EF equality, SQL collation, query source and cache/API shape | Unchanged |

Many-to-many pair validation catches equivalent complete keys even when join rows have different
primary IDs. Null preservation/guarded-only target policies remain independent. Required references,
shape/type/arity checks and cancellation are retained; validation/comparison failures precede
relationship assignment. Related rows keep the existing source/join order, not comparer order.

Only declared/accessed edges load from coordinated detached snapshots. Root/related visibility,
pages, cold streams and baseline isolation keep their existing rules. Fresh saved single/batch
DTOs reuse the same read policy when their commands are otherwise portable. This does not permit
changing immutable principal/FK keys or simulate graph writes.

## Factories and metadata

Use StringComparer.Ordinal/OrdinalIgnoreCase, EqualityComparer<T>.Default, or a self-contained
construction of an independent public IEqualityComparer<T>. Each factory is emitted, not invoked
during generation. It is evaluated per loaded relationship per snapshot, outside the store lock.
The adapter performs typed component equality/hashing without reflection-based member lookup.
Authors are responsible for a pure, stable equivalence relation with compatible hashing.

Captured comparers, ambient/culture factories, arbitrary static getters, calls, custom conversions,
private constructors, entity arguments and infrastructure types are rejected. These restrictions
inspect construction, not arbitrary comparer method bodies. Explicit factories are consumer code,
not a proof that every custom comparer is pure or equivalent to a database.

Select an ordinary non-null primary/alternate-key scalar with its exact CLR type. Array keys,
nullable CLR key types, FK-only fields and ordinary payload fields are outside this slice. Every
selected component still needs a public getter or an explicit mapped scalar slot.

A declaration can explicitly acknowledge compatible configured EF **CLR** key comparers for
the corresponding principal and FK components. It cannot acknowledge converters, provider
comparers, inconsistent/unknown metadata, shadow keys, inheritance, owned/shared joins or model
filter/AutoInclude semantics. Those still require typed overrides or real-provider fixtures.

- RGD4501: missing store, duplicate/invalid principal component or nonportable factory.
- RGD3302 / RGD3402: remaining query / saved-command behavior is not covered in strict mode.

Receipts add keyComparison, or sourceKeyComparison/targetKeyComparison for join edges, only when
configured. They identify relationship/uniqueness scope, unchanged query/store/write semantics,
the no-null-match rule and no inferred provider equivalence. Unconfigured outputs stay identical.

## Relational verification is still necessary

Shared tests execute actual generated SQLite repositories with explicit NOCASE columns and, where
appropriate, explicit CLR comparers. They cover selected ASCII references, both many-to-many
directions, nullable targets, pages and fresh saved single/batch DTOs. They also deliberately show
that `row.Code == input` is not rewritten: it remains ordinal in CLR while the configured database
may compare it differently. This feature is relationship simulation, not universal SQL parity.

Use [real-provider fixtures](REPOSITORY-CONTRACT-FIXTURES.md) for actual collation/Unicode semantics,
constraints, concurrency and provider guarantees. See [store key identity](INMEMORY-KEY-COMPARERS.md)
and [tracked scalar equality](INMEMORY-VALUE-EQUALITY.md) for the other independent policies.
