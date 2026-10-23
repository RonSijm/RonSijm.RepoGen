# Native results from mapped fields and indexers

Start with [CapturedNotesRepositoryDesigner](Configuration/CapturedNotesRepositoryDesigner.cs).
It generates two native write operations that return a field-backed computed title, a field-only
version and an indexer value through ordinary application DTOs. The consumer never supplies
mapped-property names or EF expressions.

## Designer → generated output

The [generation runner](../GenerationRunner/Program.cs) pairs this designer with
[CapturedNotesContext](Model/CapturedNotesContext.cs) and writes these exact artifacts:

| Declaration | Generated output | Effect |
| --- | --- | --- |
| Closed contracts, `ReadEnabled = false`, `BindWritePort<ICapturedNoteWrites>()` | [CapturedNotesRepositories.g.cs](../Contracts/Generated/CapturedNotesRepositories.g.cs) | Only write ports; `RenameAsync` and `RemoveAsync` inherit the application-owned interface |
| `Update` / `Delete`, named `ExpectVersion`, `ReturnsMany`, `IncludeMappedProperties()` | [Repositories.g.cs](Generated/Repositories.g.cs) | Conditional native DML and mapped scalar buffers; no fetch/save fallback |
| Explicit mapped capture | [manifest.json](Generated/manifest.json) | Capture permission, scalar access kinds, getter-free result mapping and unchanged key/setter/fixture boundaries |
| `IncludeApiDataContracts()` and runner snapshot option | [RepositoryApi.json](Generated/RepositoryApi.json) | EF-free signatures and request/result data shapes |

The important result declaration is:

```csharp
command.ExpectVersion<int, CapturedNoteRequest>("version", request => request.ExpectedVersion)
    .ReturnsMany(row => new CapturedNoteChange(
        row.Id, row.Title, row.NormalizedTitle,
        EF.Property<int>(row, "version"), EF.Property<string?>(row, "SourceTag")))
    .IncludeMappedProperties();
```

`NormalizedTitle` has a getter but no setter; EF maps its private backing field. The getter adds
a display prefix, but the designed native result receives the persisted scalar **without** that
getter behavior. `version` is a private field with no CLR property. `SourceTag` is stored through
the entity's string indexer. A fourth mapped value, `Archived`, is a normal shadow filter column.

Application code uses [ICapturedNoteWrites](Contracts/ICapturedNoteWrites.cs):

```csharp
var result = await writes.RenameAsync(
    new CapturedNoteRequest(id, title, expectedVersion), cancellationToken);
```

## What the consumer verifies

[Program.cs](Program.cs) creates a fresh SQLite database and checks computed native values,
indexer data, explicit version advancement, stale/hidden-row guards, parameterized text,
pending tracked field/indexer isolation, pre-delete values and caller-owned transaction rollback.
The SQLite token is computed from an explicitly incremented counter, not SQL Server rowversion
emulation. Native writes neither save nor synchronize pending entities.

This is a broader permission than `IncludeShadowProperties()`, not a change to that existing
option. It is off by default and requires a mapped-state-capable writer. Keys/setter targets remain
ordinary CLR properties. Immutable fields, navigation graphs, constructor-only entities and
automatic fixture field/indexer/shadow state are not enabled.

Run `dotnet run --project samples/ConfigurationExamples/MappedReturning -c Release` from the
repository root. After changing inputs, regenerate with
`./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`.

The shared Models, Contracts and Designers projects compile the colocated handwritten inputs.
The consumer never executes its designer and needs no cache, resolver, retry or fixture backend.
See [mapped native result values](../../../docs/implemented/writes/RETURNING-MAPPED-VALUES.md)
for custom-writer capabilities, exact supported mappings and transaction safeguards.

[Example index](../README.md)
