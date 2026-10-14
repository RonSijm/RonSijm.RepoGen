# Optional shared owner/reference columns

`ReconcileLinks(...)` can opt into owner and target foreign keys that share ordinary columns.
For example, Folder and Resource each have a (PartitionId, Id) key, while the join has
(PartitionId, FolderId, ResourceId). The same PartitionId belongs to both FK roles.

Start with [ScopedResourcesRepositoryDesigner](../../../samples/ConfigurationExamples/MemorySharedLinks/Configuration/ScopedResourcesRepositoryDesigner.cs)
and its [exact designer → output map](../../../samples/ConfigurationExamples/MemorySharedLinks/README.md).
This is a bounded extension to [explicit join-row reconciliation](LINK-RECONCILIATION.md), not
a tenant resolver, automatic authorization policy or general graph mapper.

## Configure it explicitly

```csharp
entity.HasCommand("ReplaceResources")
    .Where<ChangeFolderResources>((folder, request) =>
        folder.PartitionId == request.PartitionId && folder.Id == request.FolderId)
    .ReconcileLinks<StoredFolderResource, ResourceReference, (int, int), ChangeFolderResources>(
        folder => folder.Links, request => request.Resources,
        link => new ValueTuple<int, int>(link.PartitionId, link.ResourceId),
        item => new ValueTuple<int, int>(item.PartitionId, item.ResourceId),
        links => links.AllowSharedReferenceColumns()
            .MaximumItems(100)
            .Add(item => new StoredFolderResource {
                PartitionId = item.PartitionId, ResourceId = item.ResourceId,
                Caption = item.Caption, Version = 1 })
            .Update(setters => setters.SetProperty(link => link.Caption,
                (link, item) => item.Caption))
            .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
    .Save();
```

Services call the generated `ReplaceResourcesAsync(request)`. They do not configure FK roles,
provide entities or add runtime EF expressions. Use `AllowSharedReferenceColumns(false)` to
disable the option again. It is off by default and invalid on ordinary ReconcileCollection.
Enabling it on disjoint FKs has no effect: no guard or receipt is emitted.

## Complete reference matching, immutable ownership

The target selector must still select **every** target FK component, in metadata order, as one
scalar or a two-to-seven-part ValueTuple. Shared components are not omitted from matching merely
because the selected owner already supplies them.

| Mapping | Example |
| --- | --- |
| Owner FK | Join.PartitionId / FolderId → Folder.PartitionId / Id |
| Target FK | Join.PartitionId / ResourceId → Resource.PartitionId / Id |
| Complete matching key | (PartitionId, ResourceId) |
| Required consistency | Each input PartitionId equals the selected folder's PartitionId |

Generated typed predicates validate **all supplied reference keys before that collection is
loaded or mutated**. A mismatch throws ArgumentException; it is not filtered away, normalized,
treated as a new link, or allowed to transfer a link to another owner. Earlier unrelated pending
work is preserved, and earlier mutations within the current command are restored on failure.
An omitted collection skips these checks; an empty supplied collection still follows Keep/Reject/Delete.

Checks use the same default CLR equality as collection matching. They are not SQL collation,
fixture-key comparison, a custom tracked scalar-equality policy or permission to access a resource.
The application's root selection and explicit authorization remain its responsibility.

Add maps every target component, including shared columns. A mapper must preserve the complete
supplied key **before** owner binding; a wrong shared component is rejected instead of silently
overwritten. Ownership is then bound from the selected owner's complete principal key.
Updates still cannot change any owner/target FK, matching key or primary key, even with AllowWrite.
Non-shared ownership columns remain forbidden in create mappings.

Multiple shared columns, non-adjacent target-key positions, different owner-key property names/order
and complete alternate principal keys are supported. Shared owner/property CLR types must match
exactly, and the target FK must have at least one independent component. The existing non-null,
unique, non-generated matching-key rules and ordinary keyed leaf-entity boundaries still apply.

## EF and optional fixtures

Production uses EF's owner-scoped relationship query and actual constraints. Generated checks read
the selected owner's mapped principal values, including supported shadow properties, rather than
assuming identical principal/dependent CLR property names. A SQL FK still determines whether the
referenced resource exists. Target visibility is not silently injected into mutation authorization.
No resource entity is created, updated or deleted.

Fixtures reuse the same complete-key guards with explicitly declared public/entity-shaped stores
and scalar adapters. The existing `SimulateCollectionWrites()` opt-in is still independently
required; enabling shared columns does not enable testing output or automatic commands.
Public mapped stores keep internal persistence entities out of the consumer.
Fixtures do not enforce SQL FKs, unique constraints, triggers, locking or native races.

Versions, missing-item policies, bounds, cancellation, shared Stage/Save, checkpoint/baseline reset,
saved results and commit-aware cache behavior retain their existing contracts. Saving is a shared
unit-of-work save, not a single SQL statement or a repository-scoped save. Before-save/save failures
restore command-assigned values, join membership and EF fixup without discarding unrelated pending edits.

## Receipts, diagnostics and limits

Configured overlapping links add `linkReconciliation.sharedReferenceColumns` to the manifest:
dependent column, owner principal property, zero-based reference-key position, complete-key
validation timing, default CLR comparison, owner-derived binding and no normalization/transfer.
Unconfigured and disjoint links keep byte-identical output and existing contracts.

- RGD3902: missing explicit permission, option on an ordinary collection, incompatible shared CLR
  types, or a target FK with no independent component.
- Existing RGD3901/RGD2801/RGD2802: incomplete/reordered matching, unsupported FK/entity shapes,
  invalid creation, updates, versions, uniqueness or limits.
- Existing RGD3402: missing fixture opt-in/adapters or remaining nonportable behavior in strict mode.

Explicit [named additional reference roles](ADDITIONAL-LINK-REFERENCES.md) now separately support
required or explicitly [optional](OPTIONAL-ADDITIONAL-REFERENCES.md) disjoint extra FKs with create-only initialization; shared-column permission still concerns
only the owner and matching target.
Optional matching targets/undeclared-extra-FK/owned/inherited/shared-property-bag joins, direct skip-navigation mutation,
nested/cascading graphs and inferred unlink/soft-delete policies remain extensions/adapters.
Actual generated SQLite contract cases cover valid and cross-owner references, aliases/order,
alternate target keys, versions, shared saves, rollback and failed-save fixup with automatic
change detection enabled/disabled. They do not establish equivalence for every provider/collation.
