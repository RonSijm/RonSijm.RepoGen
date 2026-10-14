# Optional additional link references

`OptionalAdditionalReference(link => link.Origin)` opts a named link command into a nullable,
immutable additional FK. It extends [named link-reference roles](ADDITIONAL-LINK-REFERENCES.md),
not the matching identity or ownership of a link.

Start with [OptionalAssignmentsRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryOptionalReferences/Configuration/OptionalAssignmentsRepositoryDesigner.cs)
and its [exact designer → output map](../../../samples/ConfigurationExamples/MemoryOptionalReferences/README.md).
This write example is distinct from [MemoryOptionalLinks](../../../samples/ConfigurationExamples/MemoryOptionalLinks/README.md),
which demonstrates nullable many-to-many **reads**.

## Designer configuration

```csharp
links.TargetReference(link => link.Subject)
    .OptionalAdditionalReference(link => link.Origin)
    .Add(item => new StoredAssignment {
        SubjectId = item.SubjectId,
        OriginPartition = item.OriginPartition,
        OriginCode = item.OriginCode,
        Caption = item.Caption,
        Version = 1
    });
```

There is no global switch and nothing is enabled automatically. `AdditionalReference` still
requires a required FK; `OptionalAdditionalReference` requires an optional FK. Both declarations
share duplicate checks and `WithoutAdditionalReference(navigation)` removal. One command can
mix required and optional additional references, and each non-owner FK must be named exactly once.
The matching `TargetReference` and selected owner remain required.

Every component of an optional FK must be nullable in both the EF model and CLR representation,
non-generated, and disjoint from ownership, matching target and other additional references.
Ordinary non-unique many-to-one references and composite/alternate principal keys are supported.
No relationship value is supplied by a database/fixture factory or an inferred constructor default.

## Explicit all-or-none absence

Every additional FK component must appear in Add, even if its value is explicitly null.

| New-link key | Meaning |
| --- | --- |
| All components null | No reference |
| All components non-null | Reference to the complete declared principal key |
| Some components null | ArgumentException; no successful staging/save |

This is an explicit RepoGen command policy. Some databases treat a partial-null composite FK as
non-referencing too; the generated command deliberately rejects that ambiguous representation.
Single-column nullable references naturally have only absent/present states. Null checks use typed
`is null` tests, not reflection, equality operators, key normalization or fixture-key comparers.

The approved Add initializer is evaluated once, only for newly added links. Composite checks run
on its resulting scalar values before that row is staged. They do not scan or normalize old data,
load related entities, or execute extra SQL to validate referenced-row existence.
For optional composite creation, fixtures also prepare all new initializers before matched-row
version checks, matching production failure precedence without reserving generated IDs early.
Parent selection/version preconditions can still short-circuit with NotFound/Conflict.

**Matched rows retain their entire original reference, including presence/absence.** Add-only request
values are neither checked nor applied on a match; even a partial-null unused creation value cannot
change that reference. Only designer-approved payload/version updates run. Changing a reference is
a different reviewed operation, not a side effect of this declaration.

## Production and fixtures

Existing limits, complete target matching, visibility, missing-item policies, versions,
shared Stage/Save, transactions/savepoints and failure restoration remain in force.
Partial-key failures restore parent/link changes and retain earlier unrelated pending work.
Production permits entirely absent references; present invalid keys are still rejected by database
constraints. Constraint/save failures restore optional relationship fixup too.

Public mapped and entity-shaped fixtures use the same initializer/null checks with independently
configured `SimulateCollectionWrites()`. They preserve pending-versus-saved state and checkpoint
rollback, but do not invent SQL FK enforcement, authorization or referenced objects.
Declaring a reference does not implicitly declare navigation reads; configure those separately if
needed. The focused example projects nullable scalar FK values to keep that distinction visible.

Optional role receipts add `optional`, `nullSemantics` and initializer-validation timing only to
the configured reference. Required-reference and unconfigured generated output remains unchanged.

## Diagnostics and boundaries

- RGD4201: required/optional cardinality mismatch, optional matching target, role omissions,
  duplicate/unmapped/owner roles, additional overlap or unsupported relationship/entity shape.
- RGD4202: Add omits a required or optional additional FK component.
- RGD4301: optional components cannot all represent absence, or a FK component is generated.
- Existing write-protection, immutable-key, graph and fixture-coverage diagnostics still apply.

Mixed-nullability composite FKs, generated reference columns, optional matching targets,
overlapping additional FKs, owned/inherited/shared/implicit joins and nested graph mutation remain
extensions/adapters. No endpoint creation/deletion, business workflow or provider-equivalence
claim is added. Generated SQLite contract tests exercise absent/present states, immutable matches,
partial-key rejection and FK/check failures with change detection enabled/disabled.
