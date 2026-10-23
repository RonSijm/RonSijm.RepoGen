# Shadow concurrency tokens through EF-free ports

Start with [VersionedNotesRepositoryDesigner](Configuration/VersionedNotesRepositoryDesigner.cs).
The model's `Version` token has no CLR member. Each command binds its expected value once in
the designer; the service supplies an ordinary [NoteVersionChange](Contracts/NoteVersionChange.cs).

## Designer → generated output

The [generation runner](../GenerationRunner/Program.cs) pairs this designer with
[VersionedNotesContext](Model/VersionedNotesContext.cs) and writes these exact artifacts:

| Declaration | Generated output | Effect |
| --- | --- | --- |
| Closed contracts and `BindWritePort<IVersionedNoteWrites>()` | [VersionedNotesRepositories.g.cs](../Contracts/Generated/VersionedNotesRepositories.g.cs) | EF-free `FindAsync`, `RenameAsync`, `RenameNativeAsync` and `RenameManyAsync` signatures |
| `Patch(...).ExpectVersion<int, NoteVersionChange>("Version", ...).Save()` | [Repositories.g.cs](Generated/Repositories.g.cs) | Tracked original-state check and save; stale requests report `Conflict` |
| `Update(...).ExpectVersion(...).ReturnsMany(...).IncludeShadowProperties()` | [Repositories.g.cs](Generated/Repositories.g.cs) | Native conditional write returning a fresh version; a stale request affects zero rows |
| `PatchMany(... items => items.ExpectVersion<int>("Version", ...))` | [Repositories.g.cs](Generated/Repositories.g.cs) | Bounded, per-item version validation before the batch saves |
| Named token declarations and API snapshot | [manifest.json](Generated/manifest.json), [RepositoryApi.json](Generated/RepositoryApi.json) | Design-time binding receipts, command policies and application request/result shapes |

The root command binds a token without assigning it:

```csharp
.ExpectVersion<int, NoteVersionChange>("Version", request => request.ExpectedVersion)
```

Its batch item counterpart is:

```csharp
items.ExpectVersion<int>("Version", item => item.ExpectedVersion)
```

Application code uses [IVersionedNoteWrites](Contracts/IVersionedNoteWrites.cs):

```csharp
var result = await writes.RenameAsync(
    new NoteVersionChange(id, "new title", expectedVersion), cancellationToken);
```

No service-side EF expression or token-name string is required. `ReturnsSaved` performs a fresh
post-save projection. Native shadow-valued results independently require `IncludeShadowProperties()`;
an expected-version declaration alone never grants that permission.

## Token policy and executable checks

This SQLite model computes `Version` as `ChangeCounter + 1`. Every designed update deliberately
increments `ChangeCounter`. This is an example application policy, not automatic token advancement
or a simulation of SQL Server `rowversion`; other writers must follow the same policy.

[Program.cs](Program.cs) checks tracked and native success, stale versions, hidden rows, fresh
computed tokens, a conflicting batch with no partial save, and ordered successful batch results.
The model filter remains effective. Native writes do not synchronize already tracked entities;
the consumer clears setup state before moving between independent test cases.

Run `dotnet run --project samples/ConfigurationExamples/ShadowVersions -c Release` from the
repository root. After changing inputs, regenerate using
`./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`.

The shared Models, Contracts and Designers projects compile the colocated handwritten inputs.
The consumer never executes the designer. No resolver, cache, retry policy or automatic fixture
shadow state is enabled. See [mapped-property bindings](../../../docs/implemented/repositories/MAPPED-PROPERTY-BINDINGS.md)
for child collections, exact type checks and fixture override requirements.

[Example index](../README.md)
