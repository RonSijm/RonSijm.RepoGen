# Optional references with shared required ownership

`OptionalAdditionalReference(...)` can combine with `AllowSharedReferenceColumns()` when an optional
reference shares columns with the selected owner. This is independently opt-in: no new default,
global infrastructure, service-side expression or fixture permission is introduced.

Start with [ScopedReviewRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryScopedOptional/Configuration/ScopedReviewRepositoryDesigner.cs)
and [its exact four outputs](../../../samples/ConfigurationExamples/MemoryScopedOptional/README.md).
The optional reviewer FK is (ReviewerRegion, PartitionId, ReviewerCode), while PartitionId also
belongs to required folder ownership. Only ReviewerRegion/ReviewerCode represent optional presence.

## Configure complete scalar values

```csharp
links.TargetReference(link => link.Resource)
    .OptionalAdditionalReference(link => link.Reviewer)
    .AllowSharedReferenceColumns()
    .Add(item => new ReviewLink {
        PartitionId = item.PartitionId,
        ResourceId = item.ResourceId,
        ReviewerRegion = item.ReviewerRegion,
        ReviewerCode = item.ReviewerCode,
        Caption = item.Caption });
```

The owner and matching target remain required. Every non-owner FK still has exactly one declared
role, and Add must explicitly initialize every additional FK column, including shared columns.
One scalar assignment supplies each shared column across all its roles. Matched FKs remain immutable.

Independent components must be nullable in both EF metadata and CLR representation. Every component
must be non-generated. A reference must have at least one independent component. Only owner-shared
components can remain non-nullable; an unrelated non-nullable component is not an inferred default.
Aliases, alternate principal keys and interleaved/reordered FK components use their actual metadata.

## Absence is not nullable ownership

| New-link values | Meaning |
| --- | --- |
| Independent components all null; shared owner values correct | No optional reference; owner columns remain complete |
| Independent components all present; shared owner values correct | Complete reference using the selected owner's shared values |
| Some independent components null | ArgumentException before successful staging/save |
| Shared owner value disagrees, including an absent reference | ArgumentException before owner binding; no normalization or transfer |

Single independent nullable components naturally have only absent/present states. Multi-component
checks use typed null tests on independent columns only, not reflection or null tests on required
owner values. The approved initializer is evaluated once for each new link; its values are checked
before binding, generated fixture values, matched child versions or mutation. Root selection/version
preconditions still precede collection work.

EF treats a composite FK with any null component as absent; see its
[composite FK documentation](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/one-to-many#one-to-many-with-composite-foreign-key).
RepoGen deliberately imposes the stricter all-or-none **creation policy on independent components**.
It does not scan, repair or normalize preexisting partial-null rows. Payload updates retain their
original reference values, and separately declared optional navigation reads follow ordinary null
matching. Actual generated SQLite tests preserve this distinction.

## Immutable matches and explicit permissions

Add is not evaluated on matched rows. Even a partial-null or wrong-owner Add-only input is ignored
on a match; only approved payload/version setters run. Target key components shared with ownership
still retain their independent all-input consistency check before collection loading. Updates
cannot reassign any FK/ownership/matching/primary-key component, even with AllowWrite.

Required and optional references may share owner columns with each other or the target. Optional
references cannot share **non-owner** columns with any other FK: absence/presence dependencies
between independent references are not inferred. Optional matching targets, generated reference
columns, owned/inherited/shared joins and graph persistence remain separate boundaries.

When omission, bounds, matching, Keep/Reject/Delete, versions, Stage/shared Save, transaction/savepoint
requirements and failure restoration retain their existing semantics. Before-save/reference failures
preserve earlier unrelated pending work; failed database saves restore link values and fixup.

Fixture execution independently requires its existing store/map/backend and SimulateCollectionWrites
permissions. Read/navigation/model-filter/saved-result settings remain separate. Fixtures use the
same null/owner checks but do not enforce database FK/unique/check constraints, authorization,
collations, concurrency races or durability, and never manufacture referenced entities.

## Receipts and verification

Configured owner-shared optional references add presenceColumns and ownerSharedComponents to their
existing optional/shared-column receipt. These distinguish independent nullable presence from
always-required owner consistency, including absence. Disjoint optional and required shared-reference
outputs retain their previous bytes; an unused shared-column permission has no output effect.

- RGD3902: missing shared-column permission or incompatible shared CLR/key components.
- RGD4201: invalid roles/cardinality or optional overlaps through non-owner columns.
- RGD4202: any required additional scalar component is missing from Add.
- RGD4301: independent components cannot represent absence or reference components are generated.
- Existing write-protection, immutable-key and strict fixture-coverage diagnostics still apply.

Tests compile every layout with entity-shaped and mapped public stores, compare actual generated
SQLite operations, and cover absent/present/partial inputs, owner checks outside the target key,
immutable matches, preexisting partial-null rows, saved related reads, Stage/shared Save/rollback,
FK/check failures and fixup with change detection enabled/disabled. Provider guarantees remain
real-provider tests, not a promise of portable fixture equivalence.
