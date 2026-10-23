# Explicit mapped setters in bounded batches

`PatchMany` item builders accept `SetProperty<T>(modelName, value)` and
`SetPropertyWhen<T>(modelName, value, condition)`. These are designer declarations, not field-name
inputs or micro-extension methods in application services. Nothing is generated unless configured;
no new global option, dependency or preset change is required.

The focused [MappedBatchSetters example](../../../samples/ConfigurationExamples/MappedBatchSetters/README.md)
links [ReviewEntriesRepositoryDesigner](../../../samples/ConfigurationExamples/MappedBatchSetters/Configuration/ReviewEntriesRepositoryDesigner.cs)
to its exact contracts, production implementation, fixture and manifest.

```csharp
entity.HasCommand("Revise")
    .PatchMany<ReviewEntryEdit, int, ReviewEntriesRequest>(
        request => request.Entries, row => row.Id, item => item.Id,
        setters => setters
            .SetPropertyWhen<string>("Caption", (row, item) => item.Caption, item => item.ChangeCaption)
            .SetProperty<int>("edits", row => EF.Property<int>(row, "edits") + 1)
            .SetProperty<int>("revision", (row, item) => item.Revision + 1)
            .SetProperty<string?>("review remark", (row, item) => item.Remark)
            .ExpectVersion<int>("revision", item => item.Revision),
        maximumItems: 20)
    .AllowWrite<int>("revision")
    .Save()
    .ReturnsSaved(row => new ReviewCard(row.Id, row.Caption));
```

Services call `ReviseAsync(request, cancellationToken)`. Mapped names, query expressions and EF
entry access stay in configuration/persistence. If the remark is protected, as in the example,
the command must additionally declare `AllowWrite<string?>("review remark")`.

## Exact scalar assignments

The item builder supports constants, current-row expressions and `(row, item)` expressions.
Conditional assignments use an item-only predicate. Names bind the final model property, not a
database column. Exact CLR types are required, including nullable value types; boxing or numeric
widening is not accepted. Metadata punctuation is quoted as data, never emitted as an identifier
or SQL. Existing member-based assignments are unchanged and can be mixed with named targets,
but the same property cannot be assigned twice.

Production code assigns `context.Entry(entity).Property<T>(name).CurrentValue`. Named setter
values lower exact current-row mapped members, literal `EF.Property<T>` and typed literal indexer
reads to that same tracked state. Assignments run in declaration order. Dynamic names, foreign
entity receivers and incorrect indexer/property types fail generation. EF follows the configured
property access mode: field access avoids a behavioral getter, but this is not an arbitrary getter
interceptor or expression sandbox. Opaque helper behavior remains application-owned.

Original-token checking and deliberate advancement are separate declarations. A caller-managed
token advances only through its own setter and command `AllowWrite<T>(name)`. Any expected-version
batch must have at least one unconditional setter so every matched row performs a conditional
write. Conditional omission with no other assignments/versions returns `NoChanges`.

## Existing batch guarantees and limits

The [bounded batch protocol](BATCH-COMMANDS.md) remains in force: closed named operations, an
explicit finite limit, an ordinary unique scalar matching key and explicit Save or Stage. Inputs
are validated once before context acquisition; duplicate keys, nulls and excessive input are
rejected. One filtered query selects all targets. Missing/hidden rows return `NotFound`; stale
tokens return `Conflict` without leaving earlier mapped assignments staged.

Existing snapshots restore current/original values and modification flags on pre-save failures or
target-owned concurrency conflicts. Dirty targets are rejected before mutation. Unrelated pending
changes are preserved on failure and share the eventual Save on success. Explicit assigned flags
still persist mapped changes when automatic change detection is disabled. Save is **shared unit of
work**, not entity-scoped, and does not guarantee one SQL statement or one network round trip.

`Stage()` changes only pending state. `Save().ReturnsSaved(...)` runs the existing fresh saved-row
projection query and returns DTOs in input order. Post-save projection failures still mean
persistence succeeded; they are not rollback or safe-to-retry outcomes.

Primary/alternate keys, foreign keys, unique-index identity, generated/read-only/init-only columns
and owned/shared/inherited entities remain excluded. AllowWrite does not override those guards.
DeleteMany cannot contain setters. This extension does not enable named CreateMany, child/graph,
native-returning or atomic setter targets, shadow matching keys, cascading deletion or bulk import.
Bounded [collection/link mapped setters](MAPPED-RECONCILIATION-SETTERS.md) are a separate explicit
capability with independent parent/child permissions and existing ownership/leaf-shape guards.

## Independent fixture capability

Automatic commands remain optional. Shadow, field-backed/field-only and indexer targets require
complete independently configured public-data read/write state with exact `MapInMemoryProperty`
bindings and `MapInMemoryWrites(..., useMappedPropertyState: true)`. Existing store identity stays
ordinary and explicit; production entry access does not grant fake EF state.

The [fixture batch protocol](../testing/INMEMORY-BATCHES.md) prepares detached candidate rows before
publishing pending state. It reuses translated public scalar slots, never the production
`context.Entry` expressions. Stage/shared Save, saved DTOs, original tokens, scripts, partitions,
checkpoints and baseline reset guards retain their semantics. An absent capability needs a typed
override (even for an empty store/input), or strict RGD3402. An invalid configured map remains
RGD3601. SQL constraints, transaction isolation, generated rowversions and races are not simulated.

`mappedBatchUpdateTargets` appears in the manifest only when a batch explicitly declares named
targets. It records exact names/types/shadow bindings, tracked snapshot execution and the separate
fixture/identity/generated/native/graph boundaries. RGD1311/RGD1312 retain their mapped target/read
meanings; RGD3001/RGD3002 retain the existing batch-shape and duplicate/version guards.

[Writes](README.md) · [Mapped command setters](MAPPED-COMMAND-SETTERS.md)
