# WriteSafety: explicit scalar-write protection

Start with [AuditedNotesRepositoryDesigner](Configuration/AuditedNotesRepositoryDesigner.cs).
It configures one small context and three closed write operations. No cache, resolver, lease factory,
provider extension or runtime mapper is needed.

## Designer → generated output

The [generation runner](../GenerationRunner/Program.cs) explicitly pairs the
[WriteSafetyContext](Model/WriteSafetyContext.cs) with this designer:

```csharp
Generate(writeSafety, new AuditedNotesRepositoryDesigner().CreateModel(), "WriteSafety",
    contracts: "Contracts/Generated/WriteSafetyRepositories.g.cs");
```

| Output | What to find |
| --- | --- |
| [WriteSafetyRepositories.g.cs](../Contracts/Generated/WriteSafetyRepositories.g.cs) | `IWriteAuditedNoteRepository` with `CreateAsync`, `RenameAsync` and `CorrectAttributionAsync`; no read or selector API |
| [Repositories.g.cs](Generated/Repositories.g.cs) | `AuditedNoteRepositoryOperations`, approved scalar mappings, original-version checks, saved DTOs and DI registration |
| [manifest.json](Generated/manifest.json) | Protected `CreatedBy`/`OwnerId` fields and each command's explicitly declared permissions |

| Designer declaration | Specific behavior |
| --- | --- |
| `UseSimpleDefaults().UseClosedRepositories(...)` | Direct context and named EF-free methods; no optional infrastructure is required |
| `ReadEnabled = false` | Only write and combined contracts are emitted |
| `ProtectWrite(...OwnerId...).ProtectWrite(...CreatedBy...)` | Direct assignments to these ordinary EF scalars require explicit designer permission |
| `Create(...).AllowWrite(...OwnerId...).AllowWrite(...CreatedBy...)` | Creation may set initial ownership and attribution; returns the saved generated ID |
| `Rename` maps only `Title`/`Version` | A normal rename cannot silently acquire attribution/ownership writes through a later mapper change |
| `CorrectAttribution(...).AllowWrite(...CreatedBy...)` | A separately designed correction may change attribution; its permission does not leak to `Rename` |
| `Where(...Id...OwnerId...)` | The parent selection is explicitly owner-scoped; mismatched owners return `NotFound` |
| `ExpectVersion(...).Patch(...Version + 1)` | Stale inputs return `Conflict`; successful writes return the new application-managed version |
| `Save().Returns(...)` | All three methods return `CommandResult<AuditedNoteView>` after persistence |

Try adding this to `Rename`'s setters:

```csharp
.SetProperty(note => note.CreatedBy, "Accidental overwrite")
```

Regeneration fails with `RGD2903` **before generated files are written**. A deliberate
`.AllowWrite(note => note.CreatedBy)` on that command permits it; doing so is a reviewed design change,
not a flag passed by a service. The same protection applies to create, set-based/native/atomic writes
and explicitly configured child mappings.

[Program.cs](Program.cs) only consumes the generated API:

```csharp
var renamed = await writes.RenameAsync(new RenameNoteRequest(id, ownerId, "Renamed", expectedVersion));
```

The application types live together under [Contracts](Contracts/AuditedNoteView.cs). Consumers neither
run the designer nor construct/modify EF entities. The assertions verify initial permissions,
preserved attribution, owner isolation, explicit correction and stale-version rejection.

## Defaults and limits

Protection is opt-in per scalar. `ProtectWrite(property, enabled: false)` removes a declaration;
repeated declarations are idempotent. Unconfigured output is unchanged. This adds a generation-time
guard, not runtime checks or a global audit convention. The manifest records permissions, but public
method signatures do not grow a permissions API.

Only closed repositories support the guard: public entities/selectors would allow callers to write
outside designer mappings. `AllowWrite` does not override immutable key/store-generated rules or
child ownership/matching-key protections. The application still authorizes `CorrectAttribution`,
supplies trustworthy input and decides audit/ownership policy. Existing EF interceptors and
handwritten persistence are outside this mapper guard.

See [write protection](../../../docs/implemented/writes/WRITE-PROTECTION.md) for inherited-entity policies, child permissions,
metadata boundaries and diagnostics.

## Run and regenerate

```powershell
dotnet run --project samples/ConfigurationExamples/WriteSafety -c Release
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Run -ValidateCompilation
```

Shared Models/Contracts/Designers build projects compile the adjacent handwritten inputs. The
consumer excludes them through [ExampleSources.props](../ExampleSources.props) and references no
designer project. See [all examples](../README.md) for the bootstrap layout.
