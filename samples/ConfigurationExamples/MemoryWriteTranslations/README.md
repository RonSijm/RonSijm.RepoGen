# MemoryWriteTranslations: explicit test semantics for designed commands

Start with [PatternNotesRepositoryDesigner](Configuration/PatternNotesRepositoryDesigner.cs).
It registers exact scalar replacements once, then opts **each command** into their fixture use.
The [context](Model/PatternNotesContext.cs) maps a SQL-only length function; the separate Normalize
helper has a real CLR implementation for production tracked setters/guards. Services only call
named methods on the application read/write ports.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/PatternNotesRepositories.g.cs) | Read/write repository interfaces bound to IPatternNoteReader / IPatternNoteWriter |
| [Production repositories](Persistence/Repositories.g.cs) | Original EF.Functions.Like, Normalize and SQL Length calls; real version and Stage/Save rules |
| [Fixture/test doubles](Generated/TestDoubles.g.cs) | Plain public-mapped rows, expanded setters/guards/patterns, fresh saved DTOs and typed overrides |
| [Manifest](Generated/manifest.json) | Used translation names per operation and explicit non-provider-equivalence coverage |

| Designer declaration | Generated effect |
| --- | --- |
| HasInMemoryTranslation for Like / Normalize / Length | Reusable exact overload-specific fixture definitions; production expressions untouched |
| Per-command UseInMemoryTranslations | Only that automatic command uses registered replacements; default off independently of reads |
| Public seed store + complete read/write maps | Internal StoredPatternNote is not exposed or constructed by the consumer |
| StageRename + Stage | Expanded guards/selection/setters prepare a pending update; reads still see saved rows |
| Rename + Save + ReturnsSaved | Shared save precedes the fresh DTO read; fixture Length expands without invoking a SQL-only marker |
| Expected version / visibility | Stale versions conflict and hidden rows remain not found |
| DeleteMatching + SimulateImmediateWrites | Independently enabled immediate saved-row delete; no implicit flush/refresh of staged state |

The [consumer](Program.cs) stages/saves a rename, reads its mapped public DTO, checks a saved SQL
projection and version/visibility outcomes, then rolls an immediate delete back through a fixture
checkpoint. It references only Contracts, Testing/Abstractions and DI: no EF, Models, Designers
or generator dependency.

Registrations alone do not opt writes in. Removing UseInMemoryTranslations from these commands
causes strict coverage to reject the nonportable calls; a command can instead acknowledge and
provide an explicit typed script. Simple users get none of this infrastructure unless configured.

This example is about expression policy, not an SQL emulator. Translations do not change production
behavior, infer provider collation/constraints/transactions or loosen protected/key/ownership rules.
Store/copy/read-map/write-map/generated-value adapters remain separate. See
[command translation coverage and boundaries](../../../docs/implemented/testing/INMEMORY-COMMAND-TRANSLATIONS.md)
and [exact call matching](../../../docs/implemented/testing/INMEMORY-TRANSLATIONS.md).

Run: `dotnet run --project MemoryWriteTranslations.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
