# MemoryModelDefaults: constants from the model, only when opted in

Start with [DefaultSettingsRepositoryDesigner](Configuration/DefaultSettingsRepositoryDesigner.cs).
The [context](Model/DefaultSettingsContext.cs) owns credits, boolean, enum and text defaults.
The designer opts its complete public fixture map into those constants, without a repeated factory
per property. Internal persistence models never enter the fake-only consumer's dependencies.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [Contracts](../Contracts/Generated/DefaultSettingsRepositories.g.cs) | Bound named ports without EF/entity exposure |
| [Production](Persistence/Repositories.g.cs) | Original mappings/permissions; real defaults remain EF/database-owned |
| [Fixtures](Generated/TestDoubles.g.cs) | Typed sentinel conditions, constants and existing Stage/shared Save |
| [Manifest](Generated/manifest.json) | Model origin, precedence, sentinel policy and no SQL/provider equivalence |

| Declaration | Result |
| --- | --- |
| UseInMemoryModelDefaults | Store opt-in; false/removing writes/replacing store disables it |
| Explicit creation map | Every custom-sentinel member supplied, not inferred from a constructor |
| Credits sentinel -1 | Unset gets 5; explicit zero survives |
| Bool default true | Explicit false survives its true sentinel |
| Enum Unspecified sentinel | Basic survives; Unspecified gets Standard |
| Public read/write maps | Complete immutable rows, no internal entity construction |
| Stage then CreateMany | Defaults before staging; shared Save and input-ordered results |
| ResetCredits / Seed / baseline restore | No insert-default rerun |

The [consumer](Program.cs) also checks supplied empty text and explicit rollback. Services get
only named operations, not a default-generation DSL. Explicit fixture factories can override a
model constant and run before implicit constant assignments.

SQL, identity/key/FK/token defaults, computed/binary/object values and custom conversion/comparison
are not inferred. See [semantics and boundaries](../../../docs/implemented/testing/INMEMORY-MODEL-DEFAULTS.md).

Run: `dotnet run --project MemoryModelDefaults.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
