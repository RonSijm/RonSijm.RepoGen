# Explicit mapped command setters

`SetProperty<T>(mappedPropertyName, value)` binds an approved scalar assignment to its exact
final EF model name and CLR type. It does not require a public CLR setter and does not introduce
runtime field-name inputs in services. Names belong in the RepositoryDesigner, not API requests.
The feature is optional per assignment; no preset, dependency or unconfigured output changes.

Start with [EditableNotesRepositoryDesigner](../../../samples/ConfigurationExamples/MappedSetters/Configuration/EditableNotesRepositoryDesigner.cs)
and [MappedSetters](../../../samples/ConfigurationExamples/MappedSetters/README.md), which links
one designer to its exact contracts, production implementation, fixture and manifest.

```csharp
entity.ProtectWrite<string?>("editor remark");

entity.HasCommand("Edit")
    .Where<EditNoteRequest>((row, request) => row.Id == request.Id)
    .Patch(setters => setters
        .SetProperty<string, EditNoteRequest>("Caption", (row, request) => request.Caption)
        .SetProperty<int, EditNoteRequest>("revision", (row, request) => request.Version + 1)
        .SetProperty<string?, EditNoteRequest>("editor remark", (row, request) => request.Remark))
    .ExpectVersion<int, EditNoteRequest>("revision", request => request.Version)
    .AllowWrite<int>("revision")
    .AllowWrite<string?>("editor remark")
    .Save()
    .ReturnsSaved(row => new NoteCard(row.Id, row.Caption, EF.Property<int>(row, "revision")));
```

Applications call `EditAsync(request, cancellationToken)`. EF entry access, field/indexer names
and query expressions remain in the configuration/generated persistence layer.

## Supported execution

| Declaration | Behavior |
| --- | --- |
| Patch + Save | Reads the tracked row, sets `Entry.Property<T>(name).CurrentValue`, then uses the existing shared Save boundary |
| Patch + Stage | Stages the mapped values; saved rows stay unchanged until shared Save |
| Update | Generates an EF.Property<T> target in ordinary ExecuteUpdate; no entity fetch, tracker synchronization or shared Save |
| `SetProperty<T>(name, constant)` | Exact typed constant assignment |
| `SetProperty<T>(name, row => value)` | Current-row scalar value expression |
| `SetProperty<T, TRequest>(name, (row, request) => value)` | Current-row values plus one typed request |
| `SetPropertyWhen<T, TRequest>(name, value, condition)` | Conditional tracked Patch only; existing NoChanges/expected-version guards remain |

Tracked mapped setter values lower exact mapped current-row member, literal `EF.Property<T>` and
typed literal indexer reads to EF CurrentValue access. Generated code does not call CLR getters
directly; EF follows the model's configured property access mode. Use field access, as in the
example, when a behavioral getter must not run. Unknown/dynamic names, another entity receiver or a mismatched
type fail generation. Request members remain ordinary request reads. Tracked setters retain their
declaration-order behavior; set-based value expressions follow EF's original-row update semantics.
This is not a general mapper, arbitrary getter interceptor or SQL expression interpreter.

Expected versions still check the caller's original token. They do not assign or advance it.
An application-managed token needs an explicit setter and `AllowWrite<T>(name)` on every writer
that advances it. A database-generated token remains read-only. Existing tracked snapshots retain
current/original values and earlier dirty state on pre-save failures or command-owned conflicts.
Shared-save scope and post-save result failures retain their existing meaning; this feature does
not turn tracked writes into an isolated transaction or retry stale input.

## Deliberate guards

Names refer to **model properties**, not database column names. Exact CLR types are required;
boxing, numeric widening and mismatched nullable value types are rejected. Nullable reference
state retains nullable generated entry access. Metadata names containing spaces or punctuation
are quoted as data, not emitted as identifiers or interpolated SQL.

This slice supports scalar tracked Patch and ordinary set-based Update on ordinary entities.
It rejects primary/alternate keys, foreign keys, unique-index identity, generated/read-only columns,
init-only properties and owned/shared/inherited entity shapes. AllowWrite cannot override these
boundaries. Explicit ProtectWrite still requires its own command permission. Ordinary member-based
setters and their established behavior are unchanged.

`ProtectWrite<T>(name)` and `AllowWrite<T>(name)` bind exact model CLR types too. Protection requires
closed repositories, just like member-based ProtectWrite; it is a generation-time assignment guard,
not runtime authorization. Redeclaring a named protection replaces its type; disabling it or replacing
it with the member overload clears the old typed binding.

Bounded root-row PatchMany has its own [named item setters](MAPPED-BATCH-SETTERS.md), reusing these
exact target/read guards and the existing batch snapshots, limits and Save/Stage protocol.
Bounded [parent/child reconciliation](MAPPED-RECONCILIATION-SETTERS.md) separately supports named
Patch targets and matched-child/link scalar setters, with independently scoped write permissions.
Native ReturnsMany/ReturnsSavedMany, atomic insert/upsert, Create and broader graph
setters do not gain named assignment targets. Their capture/state options are independent;
query/capture permission is not permission to mutate a mapped column. Unsupported target/lifecycle
bindings use RGD1311; invalid tracked current-value reads use RGD1312.

## Independently configured fixtures

Automatic execution requires existing stores and automatic commands. Non-public, shadow and
indexer targets additionally need complete public-data `MapInMemoryEntity`, exact
`MapInMemoryProperty` bindings and `MapInMemoryWrites(..., useMappedPropertyState: true)`.
The [mapped write-state guide](../testing/INMEMORY-MAPPED-WRITES.md) describes lossless round trips,
ordinary identity, original tokens and detached copies. Read bindings alone grant no write state.
An invalid explicitly configured map still fails RGD3601; absent capability retains typed scripts
or strict RGD3402, including when a store is empty.

Tracked fixtures reuse Stage/shared Save and saved DTO behavior. Set-based simulation also needs
per-command SimulateImmediateWrites and operates only on saved rows, retaining pending originals.
Scripts, partitions, cancellation, baselines and checkpoints keep their existing boundaries.
No EF tracker entries, constraints, generated rowversions, SQL conversion/locking or race guarantees
are inferred. Disable automatic fixture commands without changing production operations/contracts.

The manifest adds `mappedUpdateTargets` only for explicitly named assignments, with their exact
bindings, execution mode and identity/generated/native/fixture boundaries.

[Writes](README.md) · [Mapped accessors and versions](../repositories/MAPPED-PROPERTY-BINDINGS.md) ·
[Typed commands](TYPED-COMMANDS.md)
