# CompositeCollections: matching children by Code and Locale

Start with [TranslationCatalogRepositoryDesigner](Configuration/TranslationCatalogRepositoryDesigner.cs).
The [EF model](Model/TranslationCatalogContext.cs) proves that `(Code, Locale)` is unique within
each document's complete `(TenantId, Id)` ownership. A child has a separate generated ID; callers
do not need it to reconcile translations. Persistence classes are internal, and fixture arrangement
uses public scalar rows rather than seeded navigation graphs.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/TranslationCatalogRepositories.g.cs) | StageTranslations, SaveTranslations and ListTranslations; no tuple/query DSL in the service |
| [Production repositories](Persistence/Repositories.g.cs) | Bounded tracked reconciliation; input and child key delegates validate every tuple component |
| [Fixture and test doubles](Generated/TestDoubles.g.cs) | Public parent/child stores, complete owner binding, identical matching rules and explicit generated-ID factory |
| [Manifest](Generated/manifest.json) | The ordered Code/Locale components, CLR tuple equality, null policy, immutability and owner-scoped mutation boundary |

| Declaration | Effect |
| --- | --- |
| `new ValueTuple<string, string>(row.Code, row.Locale)` | Match the entire combination; C# tuple literals cannot appear in expression trees |
| The EF unique owner/Code/Locale index | Generation checks that the matching combination really identifies one visible child |
| Explicit Add and Update | Add supplies both components; updates cannot change either matching component |
| `MissingItems(Delete)` | Delete only omitted visible keys within the selected owner; the default remains Keep |
| `SimulateCollectionWrites()` | Optional execution of these same rules in the separate fixture backend |
| Public scalar read/write maps | An EF/model-free consumer can arrange immutable data without constructing persistence entities |

The [consumer](Program.cs) references Contracts, Testing and DI only. It demonstrates equal codes
in different locales, the same key under another tenant, Stage/shared Save, generated child IDs,
hidden-row preservation, version conflicts, null-key rejection and checkpoint rollback.

No new global setting is needed. Scalar matching and simple generation stay unchanged. Two to seven
distinct direct non-null mapped scalar components are supported; arbitrary key constructors, nested
tuples, arrays, nullable child columns and unproven/filtered uniqueness are rejected. Matching uses
default CLR tuple equality, not database collation or a fixture identity comparer. Owned/nested/
many-to-many graph writes and provider constraints are not inferred.
See [composite reconciliation](../../../docs/implemented/writes/COMPOSITE-COLLECTION-KEYS.md).

Run: `dotnet run --project CompositeCollections.csproj`.
Regenerate/check with the [shared example workflow](../README.md#generate-check-and-run).
