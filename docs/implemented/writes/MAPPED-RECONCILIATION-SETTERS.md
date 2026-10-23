# Explicit mapped setters in bounded reconciliation

`ReconcileCollection(...).Update(...)` and explicit `ReconcileLinks(...).Update(...)` accept
`SetProperty<T>(modelName, value)` and `SetPropertyWhen<T>(modelName, value, condition)` for matched
rows. Parent Patch mappings can also use named targets alongside those collections. Names and
recipes belong in the RepositoryDesigner; services still call closed typed operations.

This is optional per assignment. No new preset, global switch, CLI flag or runtime query DSL is
required. The [MappedCollectionSetters example](../../../samples/ConfigurationExamples/MappedCollectionSetters/README.md)
links [ReviewBoardsRepositoryDesigner](../../../samples/ConfigurationExamples/MappedCollectionSetters/Configuration/ReviewBoardsRepositoryDesigner.cs)
to its exact contracts, production code, fixture and manifest.

```csharp
board.HasCommand("Revise")
    .Where<ReviewBoardEdit>((row, request) => row.Id == request.Id)
    .Patch(setters => setters
        .SetProperty<int, ReviewBoardEdit>("revision", (row, request) => request.Revision + 1))
    .AllowWrite<int>("revision")
    .ExpectVersion<int, ReviewBoardEdit>("revision", request => request.Revision)
    .ReconcileCollection<ReviewItem, ReviewItemEdit, string, ReviewBoardEdit>(
        row => row.Items, request => request.Items, item => item.Code, edit => edit.Code,
        items => items.MaximumItems(10)
            .Update(setters => setters
                .SetPropertyWhen<string>("Caption", (item, edit) => edit.Caption, edit => edit.ChangeCaption)
                .SetProperty<int>("edits", item => EF.Property<int>(item, "edits") + 1)
                .SetProperty<int>("revision", (item, edit) => edit.Revision + 1))
            .AllowWrite<int>("revision")
            .ExpectVersion<int>("revision", edit => edit.Revision))
    .Save();
```

Applications call `ReviseAsync(request, cancellationToken)`. No setter, property-name parameter,
EF expression or implicit persistence convention enters the service API.

## Exact bindings and scoped permissions

The child builder accepts constants, current-child values and `(child, item)` expressions; conditions
are item-only predicates. Names bind final model properties, not database columns. Exact CLR types
are required, including nullable value types; boxing/widening is rejected. Null is an assignment,
not omission. Metadata punctuation is quoted as data, never emitted as identifiers or SQL.

| Declaration | Scope |
| --- | --- |
| Root command `AllowWrite<T>(name)` | That command's parent assignments only, not child writes |
| Collection/link builder `AllowWrite<T>(name)` | That child mapping on that reconciliation, not the parent or another collection |
| Entity `ProtectWrite<T>(name)` | Exact typed design-time protection for writers of that entity, including collection payloads |

These policies are not authorization and cannot bypass identity, ownership or generated-column
guards. Mixed named/member assignments cannot repeat a target. Registered type adapters expand
inside child scalar value/condition and expected-version recipes before ordinary validation.

Production assigns `context.Entry(child).Property<T>(name).CurrentValue`. Current-child mapped
members, literal `EF.Property<T>` and typed literal indexers read that same child's entry, never
the parent's. Parent values use parent state. Assignments retain declaration order within each
recipe; the existing executor reconciles children before applying the parent Patch.

EF follows configured access modes. Field access avoids a behavioral getter; property/indexer
access may still invoke configured accessors. This is not a getter interceptor or expression
sandbox. Mapped reconciliation recipes cannot traverse navigation graphs or use dynamic/unknown
names, foreign row receivers or incorrect scalar/indexer types. A loaded navigation is not a complete
database aggregate. Opaque helper behavior remains application-owned; additional graph reads belong
in explicit saved-result/read projections.

## Existing bounded protocol

The [collection](COLLECTION-RECONCILIATION.md) and [explicit-link](LINK-RECONCILIATION.md) protocols
retain closed tracked Patch, explicit Save/Stage, finite bounds, ordinary complete matching/ownership/
reference keys and existing leaf/link shape guards. Selection respects owner and visibility/model
filters, not hidden fixup membership. Duplicate/excess inputs fail before loading; oversized existing
collections fail rather than truncate. Dirty target children are rejected, including hidden pending rows.

Parent and supplied matched-child original versions remain separate from advancement. Each versioned
scope requires an unconditional setter so its row performs a conditional write. Application-managed
tokens advance only through explicit setters and scoped grants; database-generated rowversions remain
read-only. Child originals are checked before child setters. Missing-item deletion uses loaded originals,
not absent caller tokens: protect full-replacement intent with the parent aggregate version.

Keep/Reject/Delete remain explicit. A false collection `When` skips its input and mutations, not parent
setters. No assignments returns `NoChanges`. Existing snapshots restore current/original values, flags
and membership on pre-save mapping, validation, cancellation or provider failures. Unrelated pending
work is preserved. Original-version conflicts return `Conflict`; unrelated shared-save conflicts keep
their distinct exception. There is no automatic stale-input retry.

Stage changes pending state only. Save is a shared unit-of-work save, not entity-scoped or a promise
of one SQL statement/network round trip. ReturnsSaved reads a fresh DTO graph after Save with its
own projection/filter/loading choices. Post-save result/observer failures still mean committed work,
not rollback or a safe-to-retry command.

## Independent fixtures

Automatic commands remain off by default. `SimulateCollectionWrites()` additionally needs selected
writable participant stores. Non-public/shadow/indexer targets require each participant's complete
exact public-data map and `MapInMemoryWrites(..., useMappedPropertyState: true)`. Parent state,
read/relationship permission or a similarly named token on another type cannot grant child state.
Saved graph results separately need their existing read/navigation permissions.

Fixtures translate original expressions to typed scalar slots, not production EF entry code. Detached
candidate preparation, Stage/shared Save, original-token rechecks, scripts, partitions, checkpoints and
baseline-reset guards retain their semantics. Missing capability requires a typed override or strict
RGD3402 even for empty/skipped collections; invalid maps remain RGD3601. SQL constraints, FK/unique
enforcement, tracker internals, rowversions, isolation and races are not simulated. See
[mapped fixture collections](../testing/INMEMORY-MAPPED-COLLECTIONS.md).

## Limits and receipts

Named targets exclude primary/alternate/matching keys, ownership/FKs, unique-index identity,
generated/read-only/init-only columns and owned/shared/inherited shapes, even with AllowWrite.
Insert-only `ValueGenerated.OnAdd` columns remain excluded too. Link endpoints, implicit joins,
skip navigations, nested graphs, cascading descendants and native/atomic targets are not enabled.
Existing member-based setters retain their established rules.

Named updates affect matched rows only. They do not become Add initializers or run on new children.
Unknown keys still need an explicit supported Add recipe; fixtures still need complete creation
inputs/factories. No private/shadow creation state is inferred from update recipes.

Optional `mappedReconciliationUpdateTargets` receipts list each affected navigation, leaf/link kind,
bound and exact name/type/shadow bindings, plus independent scope/state boundaries. Parent named
targets retain their existing `mappedUpdateTargets` receipt. Unconfigured output is unchanged.
RGD1311/RGD1312 retain target/value-read meanings; RGD2801/RGD2802 retain reconciliation shape,
version and duplicate guards; RGD2903 retains explicit write protection.

[Writes](README.md) · [Mapped scalar setters](MAPPED-COMMAND-SETTERS.md) · [Mapped batch setters](MAPPED-BATCH-SETTERS.md)
