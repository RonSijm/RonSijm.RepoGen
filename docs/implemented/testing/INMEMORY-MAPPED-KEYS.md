# Explicit mapped relationship keys in fixtures

`HasInMemoryNavigation(..., useMappedPropertyKeys: true)` lets one detached fixture-read edge
use exact [MapInMemoryProperty](INMEMORY-MAPPED-READS.md) values as principal/FK components.
It is off by default, separately configured on every accessed navigation. Scalar bindings alone
still do not grant relationship identity, writes or tracked-state simulation.

Start with [KeyedNotebookRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryMappedKeys/Configuration/KeyedNotebookRepositoryDesigner.cs)
and its [exact designer → output map](../../../samples/ConfigurationExamples/MemoryMappedKeys/README.md).
The small example connects a field-backed alternate key to a shadow foreign key without exposing
EF, persistence entities or key expressions in its consumer.

## Configure scalar values, then permit one edge

```csharp
modelBuilder.Entity<StoredFolder>(entity =>
{
    entity.HasInMemoryStore<FolderSeed, int>(row => row.Id,
            row => new(row.Id, row.Code, row.Name))
        .MapInMemoryEntity<FolderSeed>(row => new StoredFolder { Id = row.Id, Name = row.Name })
        .MapInMemoryProperty<FolderSeed, string>(nameof(StoredFolder.Code), row => row.Code);
    entity.HasInMemoryNavigation(row => row.Entries, useMappedPropertyKeys: true);
});

modelBuilder.Entity<StoredEntry>(entity =>
{
    entity.HasInMemoryStore<EntrySeed, int>(row => row.Id,
            row => new(row.Id, row.FolderCode, row.Title))
        .MapInMemoryEntity<EntrySeed>(row => new StoredEntry { Id = row.Id, Title = row.Title })
        .MapInMemoryProperty<EntrySeed, string?>("FolderCode", row => row.FolderCode);
    entity.HasInMemoryNavigation(row => row.Folder, useMappedPropertyKeys: true);
});
```

The final EF model supplies the actual FK, principal/alternate key, component order and nullability.
The designer does not repeat matching expressions or infer relationships from seed member names.
Existing closed/scripted/store/read settings and selected participating stores remain prerequisites.
An initializer assignment and named binding cannot supply competing values for the same property.

## Supported scope

| Component | Detached fixture source |
| --- | --- |
| Ordinary public getter or ordinary scalar initializer | Existing loading behavior |
| Exact named shadow, field-backed, field-only, indexer or CLR binding | Typed read-row slot, only with this edge's permission |
| Composite/alternate key | All components in final-model order; each must have an explicit supported source |
| Explicit CLR many-to-many join | Both principal keys and both join FKs use the same per-edge permission |
| Missing binding or unsupported key conversion/comparison | Diagnostic/typed override; never a getter or reflection fallback |

Only actual mapped properties qualify. Metadata names do not become runtime lookup strings.
Generated slots avoid collisions with assigned **and unassigned** mapped CLR members; an unrelated
binding can never fabricate a missing scalar or relationship key through a generated-name collision.
Each dataset supplies its own typed recipes. Entity constructors, getters and indexers are not
invoked by mapped loading. This is fixture adaptation, not a promise of SQL/provider equivalence.

## Independent policies remain independent

- An inverse or another edge needs its own permission. Redeclaring a navigation replaces its
  settings, so calling the default overload removes the mapped-key permission. Removing an edge,
  store or binding restores the relevant override boundary; disabling reads emits no key feature.
- Null in any key component never joins. Optional-target guards or `preserveMissingTargets: true`
  keep their separate [null policies](INMEMORY-NULL-TARGETS.md). Required-reference checks, principal
  uniqueness and complete join-pair uniqueness are unchanged, even with different fixture row IDs.
- [Relationship comparers](INMEMORY-RELATIONSHIP-COMPARERS.md) still govern matching and uniqueness
  only when explicitly declared. A key binding does not acknowledge converters/provider comparers,
  change CLR equality or infer database collation.
- [Model-filter graph permissions](INMEMORY-FILTERED-NAVIGATION.md) still apply to each filtered
  dataset. Visibility stays on its designed root; related repository policies are not injected.
  Required filtered references, unsafe null use and explicit optional-target loading retain guards.
- One detached participating-store snapshot owns the loaded graph. DTO mutations do not alter
  arrangement; counted pages, cold streams, baselines, cancellation and script precedence keep
  their existing semantics. Relationship dictionaries still validate before assigning an edge.

The store's own key/copy adapters remain authoritative for Find/Seed/Remove and duplicate row IDs.
A `(Scope, Code)` relationship does not turn an integer-keyed store into a composite-keyed one.
Likewise, a transformed binding is consumer-declared fixture data, not an inverse key transformation
or an implicit synchronization with a real database.

Suitable ordinary scalar writes can read related mapped keys through their existing fresh
[ReturnsSaved](../writes/SAVED-COMMAND-RESULTS.md) navigation results. This does not permit a missing
projection member, private/shadow write state, original-token checks or navigation-dependent writes.
Saved single/batch results retain their own complete-key and projection validation.

Implicit/shared joins, owned/inherited graph shapes, direct skip-navigation mutation, cascades,
unbound keys and broader native/provider behavior remain explicit overrides or real-provider tests.

## Inspect coverage

Per-edge `mappedPropertyKeys` receipts identify consumed source, target, JoinSource and JoinTarget
bindings with exact model names/types, safe member names and access kinds. Their scope is
`DetachedReadRelationshipMatchingAndUniquenessOnly`; store identity is unchanged and tracked/provider
state is not simulated. Query-expression `propertyReads` remain separate from loader key reads.
Opting in on an all-ordinary edge records an empty binding list, not inferred extra behavior.
Unconfigured receipts and existing production contracts, SQL, API/cache shapes remain unchanged.

Missing permission/bindings retain strict `RGD3302` for queries or `RGD3402` for saved results unless
acknowledged with `RequireTestOverride(reason)`. Non-strict unsupported calls fail on empty stores
too, until a matching script/handler supplies behavior. Existing declaration/recipe `RGD4901`,
navigation `RGD3304` and comparer diagnostics continue to apply.

[Scalar read bindings](INMEMORY-MAPPED-READS.md) · [Mapped public graphs](INMEMORY-PROXY-NAVIGATION.md) ·
[Explicit joins](INMEMORY-MANY-TO-MANY.md) · [Testing index](README.md)
