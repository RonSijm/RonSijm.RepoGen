# MappedSetters: explicit mapped scalar targets

Start with [EditableNotesRepositoryDesigner](Configuration/EditableNotesRepositoryDesigner.cs).
The [one-table context](Model/EditableNotesContext.cs) maps a getter-only field-backed Caption,
a shadow revision and a nullable indexer remark. Services call typed ports; mapped names and EF
property/entry access remain in design-time configuration and generated persistence.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/EditableNotesRepositories.g.cs) | Typed read/write ports with requests and public seed/result data |
| [Production implementation](Persistence/Repositories.g.cs) | Entry CurrentValue assignments for Patch; translated EF.Property targets for ExecuteUpdate |
| [Fixture implementation](Generated/TestDoubles.g.cs) | Independently permitted complete scalar state, without persistence constructors/getters |
| [Manifest](Generated/manifest.json) | Exact mapped bindings, execution mode, identity/generated-column restrictions and fixture boundary |

| Designer declaration | Generated effect |
| --- | --- |
| `SetProperty<string>(nameof(EditableNote.Caption), ...)` | Assigns mapped field state, not the behavioral CLR getter |
| `SetPropertyWhen<string, EditNoteRequest>(...)` | Conditional tracked assignment; set-based conditions remain unsupported |
| `SetProperty<int>("revision", ...)` + `AllowWrite<int>("revision")` | Explicit caller-advanced shadow token; original-version checking remains separate |
| `SetProperty<string?>("editor remark", ...)` | Safely quoted metadata name and nullable indexer state |
| `ProtectWrite<string?>("editor remark")` + `AllowWrite<string?>(...)` | Sensitive mapped state requires explicit command-owned permission; not runtime authorization |
| `MapInMemoryProperty` + `MapInMemoryWrites(useMappedPropertyState: true)` | Separate complete fixture bindings; production permission never supplies fake EF state |

The [EF/model-free consumer](Program.cs) exercises conditional Patch, Stage/shared Save,
immediate updates, stale tokens, checkpoints and baselines. Conditional Edit may still advance
the revision and remark even when it preserves Caption; that is explicitly configured behavior.

Only scalar tracked Patch and ordinary set-based Update gain named targets. Keys, foreign keys,
unique identity, generated columns, native returning, atomic and graph setters retain their
guards. [MappedBatchSetters](../MappedBatchSetters/README.md) separately demonstrates named item
assignments under the existing bounded PatchMany protocol. This example does not enable provider
constraints, rowversion generation or races in fixtures.
Defaults and existing output remain unchanged.

Run: `dotnet run --project MappedSetters.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
See [mapped command setters](../../../docs/implemented/writes/MAPPED-COMMAND-SETTERS.md).
