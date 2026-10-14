# Optional named link-reference roles

`TargetReference(link => link.Target)` names the required dependent reference whose complete FK
defines matching identity. `AdditionalReference(link => link.Other)` declares another required,
immutable FK that may be initialized on **new links only**. [OptionalAdditionalReference](OPTIONAL-ADDITIONAL-REFERENCES.md)
separately opts nullable additional FKs into explicit all-or-none creation semantics.

This extends [join-row reconciliation](LINK-RECONCILIATION.md) to ordinary join rows with three
or more explicitly accounted-for FK roles. It does not infer a matching target from several
relationships, persist navigation graphs, implement approval workflows or authorize references.

Start with [ReferenceCatalogRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryAdditionalReferences/Configuration/ReferenceCatalogRepositoryDesigner.cs)
and its [exact designer → output map](../../../samples/ConfigurationExamples/MemoryAdditionalReferences/README.md).

## Configure every role

```csharp
entity.HasCommand("Replace")
    .Where<ChangeMemberships>((collection, request) => collection.Id == request.CollectionId)
    .ReconcileLinks<StoredMembership, MembershipInput, int, ChangeMemberships>(
        collection => collection.Memberships, request => request.Items,
        link => link.ItemId, item => item.ItemId,
        links => links.TargetReference(link => link.Item)
            .AdditionalReference(link => link.Contributor)
            .AdditionalReference(link => link.Source)
            .MaximumItems(100)
            .Add(item => new StoredMembership {
                ItemId = item.ItemId, ContributorRegion = item.ContributorRegion,
                ContributorCode = item.ContributorCode, SourceId = item.SourceId,
                Caption = item.Caption, Version = 1 })
            .Update(setters => setters.SetProperty(link => link.Caption,
                (link, item) => item.Caption))
            .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
    .Save();
```

The generated service method remains `ReplaceAsync(request)`. Its interface exposes application
requests/results, not EF entities, navigation setters or a runtime query DSL.

| Role | Configuration | Write semantics |
| --- | --- | --- |
| Owner | Selected principal-to-link collection | All ownership values are bound from the selected parent |
| Matching target | TargetReference | Complete FK in declared metadata order defines link identity |
| Additional reference | One AdditionalReference per remaining FK | Every component is mapped on Add; matched rows retain it unchanged |
| Referenced entity | Existing related table | Never created, updated or deleted by reconciliation |

Every non-owner FK must be named exactly once. Additional declarations require an explicit target;
the owner navigation cannot be reused as a target, and one reference cannot occupy two roles.
Selectors must directly select mapped dependent references, not scalar fields, nested paths,
collections, constructed objects or captured helpers.

Unconfigured ordinary two-FK links keep their original inference/output. `WithoutTargetReference()`
and `WithoutAdditionalReference(navigation)` remove declarations; an incomplete remaining role set
fails generation rather than implicitly choosing another target.

## Complete creation, immutable matches

Add must explicitly initialize every scalar FK component of every additional reference.
Composite/alternate principal keys are supported. Constructor defaults, partial initializers,
database defaults and reverse DTO mappings are not inferred.

The declared reference grants FK initialization permission, not a general property-protection bypass.
Explicit ProtectWrite policies still require local AllowWrite permission. No permission allows a
matched link to change an owner, target, additional FK, matching key or primary key.

**Creation inputs are not update setters.** If a matched item supplies a different contributor/source,
those Add-only values are not applied; only explicitly declared payload/version updates run.
Design a separate reviewed operation if changing a reference is genuinely needed.
Keep/Delete/Reject missing-item policies continue to act on visible owner-scoped join rows.

Required non-unique many-to-one, non-ownership references to ordinary keyed entities are supported;
nullable additional roles have their own explicit OptionalAdditionalReference declaration.
Additional columns must be disjoint from owner, matching target and each other. Existing
[AllowSharedReferenceColumns](SHARED-REFERENCE-COLUMNS.md) can still permit a compatible **owner/target**
overlap; it does not permit additional overlaps.
Complete target matching must remain unique globally or within the owner's complete FK.

## Production and independently optional fixtures

Production reuses tracked reconciliation, bounded relationship queries, root/join visibility,
versions, explicit Stage/Save, failure restoration, transaction/savepoint boundaries and cache tags.
Populated referenced/navigation graphs are rejected at runtime, including constructor-originated
graphs. A shared save includes unrelated pending changes; it is not a single SQL statement.

The database remains authoritative for referenced-row existence and constraints. Referenced-table
visibility is not automatically injected into authorization. No related entity is fetched merely
to decide whether it may be referenced; configure that authorization in the application/designer.

Fixtures reuse the same approved scalar mappings with existing public/entity-shaped stores and
`SimulateCollectionWrites()`. Named reference roles do not enable automatic commands, testing
output or additional global infrastructure. Internal persistence entities are not constructed by
mapped public consumers.
Related stores are needed for designed reads/saved DTOs, not to simulate SQL FK enforcement.
Fixtures may hold an invalid FK until a designed read fails or a typed script reports a provider
failure; they never silently create the missing related row.

## Receipts, diagnostics and boundaries

Configured link receipts add `targetNavigation` and `additionalReferences`: reference navigation,
target type, complete dependent/principal key columns, explicit initialization on new links only,
immutable matched FKs, no endpoint writes, database-only existence and no inferred authorization.
Unconfigured output, contracts and dependencies remain unchanged.

- RGD4201: missing/duplicate/unaccounted role, non-direct/unmapped/owner reference, unsupported
  cardinality/entity shape or additional-column overlap.
- RGD4202: an Add mapping omits any additional reference component.
- Existing RGD2801/RGD2802/RGD2903/RGD3402: matching/leaf/write/protection/fixture coverage boundaries.

Optional matching targets, overlapping additional references, owned/inherited/shared-property-bag joins,
direct skip-navigation mutation, nested/cascading graph writes and arbitrary business workflows
remain extensions/adapters. Actual generated SQLite cases cover composite alternate references,
saved related DTOs, immutable matched FKs, shared saves/rollback and FK/check-constraint failures
with pretracked fixup and automatic change detection enabled/disabled. They are not universal
provider/collation/race equivalence claims.
