# Typed mapped-property bindings

A designer can explicitly bind an accessor or expected version to an EF scalar property by its
model name, including a shadow property. The name is resolved and type-checked during generation.
It does not become a runtime service argument, dynamic query API or token setter.

Ordinary CLR-member overloads remain available. Nothing new is emitted unless these named
bindings are configured; the simple preset and inferred shadow-index behavior are unchanged.

## Mapped accessors

```csharp
modelBuilder.Entity<AliasEntry>(entity =>
{
    entity.HasAccessor<string>("SearchCode");
    entity.HasAccessor<AliasState>("State")
        .UseValueConverter<string>().HasMethodName("ByState").IsUnique(false);
});
```

`HasAccessor<TProperty>(string)` requires the **model CLR type**, before any accessor input
adapter or EF converter. `SearchCode` is a string shadow scalar; `State` is a model enum with an
existing string converter. The generated methods accept `string searchCode` and `string state`
respectively. Existing input adapters, explicit input nullability and converter checks still apply.

A single-property primary key or unique index makes the named accessor unique unless
`IsUnique(...)` overrides it. Unique accessors return a single selector; other accessors return a
collection. Other shadow properties, indexes and composite keys are not inferred automatically.
Normal selector query filters and read tracking policies remain in effect.

Automatic key/index lookups require an actual public non-indexer CLR property or public field
with the model property's name. Indexer, private/field-only and aliased model properties are not
silently emitted as nonexistent CLR members. Select them explicitly through the named binding above.

Names refer to **EF properties**, not database column names. A property containing punctuation,
spaces or other unsuitable identifier characters needs a valid explicit method name:

```csharp
entity.HasAccessor<int>("Revision stamp").HasMethodName("ByRevisionStamp");
```

Such an accessor uses `value` when a safe parameter name cannot be derived. The mapped name is
emitted as a quoted constant; lookup values are ordinary parameterized data. There is no runtime
reflection lookup or caller-supplied SQL identifier. Unknown properties, navigations, boxed/widened
types and invalid method names fail with `RGD1710` before generated files are written.

Accessors belong to selector-mode output, including CQRS selectors. Closed EF-free repositories
still expose only their configured named operations and bound ports; registering an accessor does
not add a free-form query API to a closed port.

See [MappedAccessors](../../../samples/ConfigurationExamples/MappedAccessors/README.md) for the
designer, model, three exact generated artifacts and executable SQLite consumer.

## Expected versions

Configure the model token first, for example:

```csharp
modelBuilder.Entity<VersionedNote>().Property<int>("Version").IsConcurrencyToken();
```

Then bind caller input in the repository designer:

```csharp
entity.HasCommand("Rename")
    .Where<NoteVersionChange>((row, request) => row.Id == request.Id)
    .Patch(setters => setters
        .SetProperty<string, NoteVersionChange>(row => row.Title, (row, request) => request.Title)
        .SetProperty(row => row.ChangeCounter, row => row.ChangeCounter + 1))
    .ExpectVersion<int, NoteVersionChange>("Version", request => request.ExpectedVersion)
    .Save();
```

The named overload takes input-only `Expression<Func<TRequest, TProperty>>`; it does not read the
freshly fetched entity to decide what version the caller expected. The EF property must exist,
have exactly that CLR type and be configured as a concurrency token. Nullable value types must
match exactly, such as `int?`; reference nullability follows EF/input contract metadata. Multiple
distinct tokens are allowed, duplicate tokens and create-time expected versions are rejected.
Missing or mismatched named-token metadata reports `RGD1310`; existing command policy validation
also applies.

### Tracked and immediate behavior

- Tracked `Patch` and `TrackedDelete` compare the caller's value with original tracked property
  state before modifying anything, then bind that original value for EF's save-time concurrency
  predicate. `Stage` does not save; `Save` persists and translates known conflict outcomes. The
  runtime explicitly loads tracked state even when the context defaults to no-tracking.
- Immediate set-based `Update`/`Delete`, including native `ReturnsMany`, add the token to their
  SQL predicate. No matching row means zero affected rows; absence, visibility and stale versions
  are not automatically distinguishable. A configured outcome policy may report `NotMatched`.
- A named expected version does **not** assign, increment, reload, return or retry the token.
  Database-generated tokens follow provider/model behavior. Application-managed tokens must be
  advanced deliberately by every relevant writer.

Separate [mapped scalar setters](../writes/MAPPED-COMMAND-SETTERS.md) can explicitly advance
application-managed named tokens through scalar Patch/ordinary Update, with AllowWrite<T>(name).
The version binding itself remains only an original-state check; native/batch/graph and generated
target restrictions are unchanged.

EF uses original concurrency values for tracked update/delete predicates. Database-generated
`rowversion` is provider-specific; SQLite does not provide that SQL Server facility. See
[EF optimistic concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).

Shadow values normally live in EF's change tracker. Query projections can read them with
`EF.Property<T>(row, "Name")`; a detached entity alone does not carry them. See
[EF shadow properties](https://learn.microsoft.com/en-us/ef/core/modeling/shadow-properties).
`ReturnsSaved(...)` can query a fresh token after saving. Native shadow-valued return projections
independently require [IncludeShadowProperties](../writes/RETURNING-SHADOW-VALUES.md).

### Batches and child collections

The same binding is available on a batch item or matched child item:

```csharp
items.ExpectVersion<int>("Version", item => item.ExpectedVersion);
children.ExpectVersion<byte[]>("RowVersion", item => item.ExpectedVersion);
```

These configure existing `PatchMany`/`DeleteMany` and child-reconciliation version checks; they do
not introduce a second persistence path. Bounds, ownership, immutable matching keys, validation
before mutation, Stage/Save boundaries and batch ordering stay intact. Added child items have no
previous row version to check. Broader graph/token lifecycle rules remain application decisions.

See [ShadowVersions](../../../samples/ConfigurationExamples/ShadowVersions/README.md) for tracked,
native and bounded batch writes through EF-free ports. Its SQLite token is a computed value of
an explicitly advanced counter, not automatic rowversion emulation.

## Receipts and fixture boundaries

Manifests add `mappedPropertyBindings` only for entities with explicit named bindings. It lists
accessor method names, model names/types, shadow status, uniqueness, and expected-version scopes
(`Entity`, `BatchItem`, `Collection:Navigation`). It also records that token advancement is separate
and shadow/non-public token state needs separate fixture consent or a typed override. Default manifests are unchanged.

The design model uses `RepositoryExpectedVersionDesign`, separate from scalar update assignments.
A version declaration is an original-state condition, not an instruction to overwrite current state.
Separate [named PatchMany setters](../writes/MAPPED-BATCH-SETTERS.md) can explicitly advance
eligible caller-managed scalar tokens under the existing bounded batch Save/Stage protocol;
the original token still needs its own declaration and write permission remains command-owned.

Named CLR tokens with public scalar members can reuse eligible entity-shaped or mapped-public
fixture stores. Named tokens without public CLR state need either complete bindings and separate
[mapped fixture write-state consent](../testing/INMEMORY-MAPPED-WRITES.md), or an explicit command
override. Strict automatic-command mode rejects unacknowledged unsupported tokens. The separate
fixture option does not infer token advancement, provider-generated values, concurrency races,
constraints or SQL translation. Selector accessors themselves are not closed-port fixture operations.

For named read operations, [MapInMemoryProperty](../testing/INMEMORY-MAPPED-READS.md) separately
binds exact shadow, field/indexer and ordinary CLR scalar reads to public fixture data. That getter-free adaptation does not remove
the original-token/write-state boundaries above or enable native returned buffers.

Mapped scalar bindings do not relax separate restrictions on keys, setter targets, native returned
table shapes or graph persistence. A readable EF mapped property is not automatically an allowed
native return value or mutable field. Use the relevant feature's own opt-ins and diagnostics.

Native [IncludeMappedProperties](../writes/RETURNING-MAPPED-VALUES.md) separately permits supported
field/indexer scalar results, without granting key/assignment changes or fixture state.

[Repository guides](README.md) · [Write guides](../writes/README.md) · [Documentation index](../../README.md)
