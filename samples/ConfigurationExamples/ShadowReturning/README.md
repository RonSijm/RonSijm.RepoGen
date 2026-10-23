# Shadow values from native writes

Start with [AuditDocumentsRepositoryDesigner](Configuration/AuditDocumentsRepositoryDesigner.cs).
It generates two named write methods that return database-computed audit fields without exposing
EF or shadow-property names to the service.

## Designer to output

The [generation runner](../GenerationRunner/Program.cs) pairs this designer with
[AuditDocumentsContext](Model/AuditDocumentsContext.cs). It writes these exact artifacts:

| Input | Generated output | Effect |
| --- | --- | --- |
| Closed contracts and bound `IAuditDocumentWrites` | [AuditDocumentsRepositories.g.cs](../Contracts/Generated/AuditDocumentsRepositories.g.cs) | `RenameAsync` and `RemoveAsync` return `MutationResult<AuditDocumentChange>` |
| `Update` / `Delete`, `ExpectVersion`, `ReturnsMany`, `IncludeShadowProperties` | [Repositories.g.cs](Generated/Repositories.g.cs) | Native DML with detached scalar buffers; no fetch/save fallback |
| Model and command options | [manifest.json](Generated/manifest.json) | Returned scalar inventory and explicit shadow permission, tracking and fixture boundaries |
| `IncludeApiDataContracts` and runner snapshot option | [RepositoryApi.json](Generated/RepositoryApi.json) | EF-free signatures and public request/result shapes |

The designer declares the result once:

```csharp
command.ReturnsMany(row => new AuditDocumentChange(
        row.Id, row.Title, row.Revision,
        EF.Property<string>(row, "NormalizedTitle"),
        EF.Property<int>(row, "TitleLength"),
        EF.Property<string?>(row, "SourceTag")))
    .IncludeShadowProperties();
```

Application code uses the [plain contract](Contracts/IAuditDocumentWrites.cs):

```csharp
var changes = await writes.RenameAsync(
    new AuditDocumentRequest(id, title, expectedRevision), cancellationToken);
```

`NormalizedTitle` and `TitleLength` have no CLR properties. The native returned row supplies their
updated computed values, or their pre-delete values. `SourceTag` demonstrates a nullable shadow
column with a database default. The option is off by default and scoped to each command;
`IncludeShadowProperties(false)` restores the ordinary mapping restriction.

## What the consumer verifies

[Program.cs](Program.cs) uses a fresh in-memory SQLite database and direct-context DI. It checks
computed/default values, a parameterized injection-shaped title, expected revisions, the shadow
`Archived` global filter, pre-delete values and caller-owned transaction rollback. Pending tracked
shadow edits are neither saved nor substituted into the returned DTO, and tracked copies are not
synchronized automatically.

Run `./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Run` from the repository
root to regenerate, build and run the examples. This consumer never executes its designer.

No tenant resolver, cache, retry policy or generated in-memory fixture is required here. Shadow
keys/setter targets, backing-field/indexer mappings, navigation graphs and automatic fixture shadow
state are not enabled. See the [shadow result guide](../../../docs/implemented/writes/RETURNING-SHADOW-VALUES.md)
for supported shapes and custom infrastructure writers.

[Example index](../README.md)
