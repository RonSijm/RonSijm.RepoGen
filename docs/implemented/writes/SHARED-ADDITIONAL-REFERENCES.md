# Optional shared columns in additional link references

`ReconcileLinks(...).AllowSharedReferenceColumns()` also permits explicitly declared **required**
additional references to share columns with ownership, the matching target or each other.
It is off by default. No new global setting, service-side query DSL or fixture permission is added.

Start with [SharedAuditRepositoryDesigner](../../../samples/ConfigurationExamples/MemorySharedReferences/Configuration/SharedAuditRepositoryDesigner.cs)
and [its exact four outputs](../../../samples/ConfigurationExamples/MemorySharedReferences/README.md).
This example has a global resource ID and a partition-scoped actor reference. The actor shares a
partition column with the owner, even though that column is not in the resource matching key.

## Explicit roles and complete creation

```csharp
links.TargetReference(link => link.Resource)
    .AdditionalReference(link => link.Actor)
    .AllowSharedReferenceColumns()
    .MaximumItems(100)
    .Add(item => new AuditLink {
        PartitionId = item.PartitionId,
        ResourceId = item.ResourceId,
        ActorId = item.ActorId,
        Caption = item.Caption })
    .Update(setters => setters.SetProperty(link => link.Caption,
        (link, item) => item.Caption));
```

Every non-owner FK must still have exactly one explicit role. Required ordinary non-unique
many-to-one references remain the supported shape. Every additional reference needs at least one
non-owner component, and shared dependent/principal CLR types must agree exactly.
[Owner-shared optional references](OWNER-SHARED-OPTIONAL-REFERENCES.md) now separately permit
required owner components with nullable independent presence; non-owner optional overlaps stay unsupported.

Each reference's complete FK must be explicitly mapped on Add. A shared scalar is assigned once,
not separately for each role. That one value participates in every declared FK using the column.
Property protection still applies. Populated navigation graphs, constructors as implicit reference
maps and partial additional-reference initializers are not permitted.

## Matching and creation have different validation scopes

| Check | When it runs |
| --- | --- |
| Complete target matching key | Existing input preparation; all target components stay in metadata order |
| Target components shared with ownership | All supplied target keys before collection loading/mutation, including matched keys |
| Additional reference values shared with ownership | New links only, after their initializer and before owner binding, matched child versions or mutation |
| Additional columns shared only with other references | One explicit scalar assignment serves those FK roles; existence remains database-enforced |
| Matched additional-reference inputs | Ignored; Add is not an update recipe and is not evaluated for a matched row |

Creation must agree with the selected owner's actual principal values. Production reads mapped
principal properties through EF metadata, including aliases/order and supported shadow properties.
Fixtures use the explicitly mapped public/entity-shaped row. Default CLR equality is used, not a
provider collation or fixture-key comparer. A mismatch throws ArgumentException rather than being
overwritten, normalized, filtered out or used to transfer a link.

After validation, ownership is bound from the selected owner. Updates still cannot change any
owner, target, additional-reference, matching-key or primary-key column, even with AllowWrite.
Additional input values on matched rows are deliberately not checked: an unrelated Add-only field
must not turn a valid payload update into an attempted reference reassignment.

An omitted conditional collection performs no input/creation check. Empty input follows its normal
Keep/Reject/Delete policy. Existing bounds, pending-state guards and root/child expected-version
outcomes remain; root selection/version checks precede collection reconciliation.

## Persistence, fixtures and failures

Production prepares additions before applying matched child versions/setters. Each approved Add
initializer is evaluated once. New-reference failures leave that command's parent/earlier collections
restored without discarding prior unrelated pending work. Save failures retain existing transaction/
savepoint and fixup restoration. Stage remains distinct from the shared unit-of-work Save.

Fixtures independently require the existing stores, complete public write maps, automatic command
backend and SimulateCollectionWrites permission. Their creation checks run before binding,
generated-value allocation and matched child versions. Cancellation, throwing guards and later
collection failures preserve saved rows and prior pending work. A shared-column declaration does
not grant read/model-filter/saved-result permissions.

Referenced entities are never created, updated or deleted. This consistency guard is not tenancy,
authorization or a target-existence query. SQL FKs, unique/check constraints, triggers, races and
durability remain provider guarantees, not simulated fixture behavior.

## Receipts and diagnostics

Only actual overlaps add `additionalReferences.references[].sharedColumns` to the link receipt.
It lists dependent columns, other FK roles and any owner principal property, plus explicit opt-in,
single-assignment mapping, Add-only guard timing, immutable matches and the database-constraint
boundary. Disjoint/unconfigured designs keep their generated output and contracts unchanged.

- RGD3902: missing shared-column permission, incompatible CLR components or no independent
  non-owner reference component.
- RGD4201: missing/duplicate/unaccounted roles, non-owner optional overlaps or unsupported reference shapes.
- RGD4202: an Add mapping omits any additional FK component, including a shared component.
- RGD2802/RGD2903/RGD3402: existing scalar mapping, immutable FK, protection and fixture coverage rules.

Tests compile every output layout and both fixture shapes. Shared contracts run actual generated
SQLite repositories for alternate/composite references, aliased/reordered owner keys, shared columns
outside the target key, immutable matches, saved DTOs, shared saves/rollback and failed-save fixup
with automatic change detection enabled/disabled. Runtime cases additionally check initializer
ordering, cancellation and preservation of prior pending work; they do not promise provider parity.
