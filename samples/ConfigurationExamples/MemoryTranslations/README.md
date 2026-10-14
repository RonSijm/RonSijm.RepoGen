# MemoryTranslations: explicit provider/helper behavior in fixtures

Start with [PatternCatalogRepositoryDesigner](Configuration/PatternCatalogRepositoryDesigner.cs).
The [context](Model/PatternCatalogContext.cs) maps [PatternFunctions.Length](Model/PatternFunctions.cs)
to SQLite's native `length` function. The designer declares the fixture replacement as `value.Length`
and deliberately chooses ordinal-ignore-case `%`/`_` pattern behavior. It does not infer a database collation.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/PatternCatalogRepositories.g.cs) | Named Search/Summaries/Escaped/Totals/Stream methods and the application port |
| [Production repositories](Persistence/Repositories.g.cs) | Original EF.Functions.Like and PatternFunctions.Length calls, not fixture helpers |
| [Fixture/test doubles](Generated/TestDoubles.g.cs) | Plain mapped rows, InMemoryStringPatterns calls, scalar Length expansion and typed scripts |
| [Manifest](Generated/manifest.json) | Per-operation used translation names, exact matching and explicit non-provider-equivalence receipts |

| Designer declaration | Generated effect |
| --- | --- |
| `HasInMemoryTranslation(... EF.Functions.Like ..., ... InMemoryStringPatterns.Like ...)` | Only fixture predicates use the explicitly chosen pattern policy |
| Separate Like overload with fixed `"!"` | Only that exact method overload/escape literal is replaced |
| `PatternFunctions.Length(value)` → `value.Length` | Fixture predicates, DTO projections and group keys avoid invoking a SQL-only helper |
| Public seed store + MapInMemoryEntity | Internal persistence types are not constructed or referenced by the consumer |
| Visibility/guards/order/named projection | Normal designer rules still apply after translation |
| UseInMemoryTestQueries + strict coverage | Unsupported calls cannot silently return an empty/default result |

The [consumer](Program.cs) references only Contracts, Testing/Abstractions and DI. It checks raw patterns,
minimum lengths, hidden rows, nullable summaries, explicit escaping, grouped totals and cold streams.
The production helper throws if executed in CLR code; successful fixture execution therefore also
demonstrates that the generator expanded the call instead of invoking that helper.

Production APIs, SQL definitions/cache fingerprints and simple configurations remain unchanged.
All test generation is off by default. This example focuses on named reads; the separate
[MemoryWriteTranslations](../MemoryWriteTranslations/README.md) designer opts commands in explicitly.
Store/value adapters are not rewritten. SQL/code-point/collation/constraint behavior still needs
actual provider verification. See [translation semantics and limits](../../../docs/implemented/testing/INMEMORY-TRANSLATIONS.md).

Run: `dotnet run --project MemoryTranslations.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
