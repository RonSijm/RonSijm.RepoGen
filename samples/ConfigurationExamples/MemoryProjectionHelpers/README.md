# MemoryProjectionHelpers: original mapper, explicit fixture DTO expression

Start with [ProjectionMemosRepositoryDesigner](Configuration/ProjectionMemosRepositoryDesigner.cs).
The [context](Model/ProjectionMemosContext.cs) contains one internal memo model. Production calls
[MemoCards.Create](Contracts/MemoCards.cs); fixtures expand the explicitly registered scalar-to-DTO
expression without constructing the internal persistence model.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/ProjectionMemosRepositories.g.cs) | Handwritten read/write ports, MemoCard and explicit outcomes |
| [Production repositories](Persistence/Repositories.g.cs) | Original MemoCards.Create call, not its fixture replacement |
| [Fixtures](Generated/TestDoubles.g.cs) | Flat MemoCard construction from scalar rows, without invoking the original mapper |
| [Manifest](Generated/manifest.json) | Exact matching, ExplicitFlatDtoConstruction and no provider-equivalence claim |

| Designer declaration | Generated effect |
| --- | --- |
| HasInMemoryProjectionTranslation | Explicit fixture-only DTO recipe; neither lambda runs during generation |
| HasProjection/ProjectTo<MemoCard> | One named original mapping reused by list/find/page/stream |
| UseInMemoryTranslations on Rename | Independently enables expansion for fresh saved DTOs |
| Scalar store/read/write maps | Immutable public seeds; no model/EF consumer dependency |
| Visibility/ExpectVersion | Hidden/missing/conflict outcomes remain separate from result mapping |

The [consumer](Program.cs) checks formatting, counted pages, missing results, saved DTOs,
unchanged persisted text, version conflicts, cold snapshot-stable streams and baseline reset.
It references Contracts, Testing/Abstractions and DI, not EF/models/designers/generation tooling.

This is not automatic graph mapping or method-body inspection. The fixture recipe is explicit
and production remains unchanged. An opaque helper in a SQL filter/intermediate ordering can
still fail: no client filter fallback is added. See [boundaries](../../../docs/implemented/testing/INMEMORY-PROJECTION-TRANSLATIONS.md).

Run: `dotnet run --project MemoryProjectionHelpers.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
