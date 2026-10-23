# MemoryScalarEquality: one explicit tracked comparison policy

Start with [ComparisonNotesRepositoryDesigner](Configuration/ComparisonNotesRepositoryDesigner.cs).
The [context](Model/ComparisonNotesContext.cs) configures CLR value comparers for nullable Caption
and string Revision. The designer independently declares their fixture equivalents. Production
token SQL collation is a separate SQLite-specific choice; Caption queries remain literal equality.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/ComparisonNotesRepositories.g.cs) | Read/write interfaces bound to the handwritten application ports |
| [Production repositories](Persistence/Repositories.g.cs) | Original predicates, version bindings and tracked Stage/Save; no fixture helpers |
| [Fixture/test doubles](Generated/TestDoubles.g.cs) | Typed scalar-equality helpers, mapped public rows and unchanged copy/round-trip validation |
| [Manifest](Generated/manifest.json) | Explicit tracked-only scalar policies; unchanged queries/keys/relationships/production |

| Designer declaration | Generated effect |
| --- | --- |
| Public store + MapInMemoryEntity / MapInMemoryWrites | Internal ComparedNote is neither exposed nor instantiated by the consumer |
| Caption equality using OrdinalIgnoreCase | Equivalent non-forced updates save zero rows and preserve the stored spelling |
| Revision equality using OrdinalIgnoreCase | Tracked expected versions/save checks use the explicit policy; stale versions still conflict |
| Matching predicate `row.Caption == caption` | Remains ordinary query equality, not the tracked-state comparison policy |
| StageCaption / shared Save | Staged equivalent values do not become persisted changes |
| Rename + ExpectVersion + Save + ReturnsSaved | A protected version assignment remains explicit; fresh DTOs describe saved state |
| Delete + tracked expected version | Token policy applies without introducing immediate SQL/race semantics |

The [consumer](Program.cs) checks equivalent/nullable values, unchanged query matching, staged saves,
case-varied expected versions, stale conflicts, saved DTOs and rollback. It references only Contracts,
Testing/Abstractions and DI: no EF, Models, Designers or generator dependency.

Remove a policy with WithoutInMemoryValueEquality. Without matching policies, strict automatic
tracked commands for this custom-comparer model require an acknowledged typed script. Simple
models need no policy and emit no extra helper. The option never loosens copy/write-map round trips,
keys/unique/FK matching, generated-value or provider-comparison contracts.

See [scalar comparison semantics and boundaries](../../../docs/implemented/testing/INMEMORY-VALUE-EQUALITY.md).
Run: `dotnet run --project MemoryScalarEquality.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
