# MemoryNestedProjections: nested data from a configured mapper

Start with [BriefingsRepositoryDesigner](Configuration/BriefingsRepositoryDesigner.cs).
The [model](Model/BriefingsContext.cs) stores scalar briefing rows. The application mapper returns
a heading, a fact collection and a word array; these are result DTOs, not EF navigations.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [Contracts](../Contracts/Generated/BriefingsRepositories.g.cs) | Application read/write ports returning BriefingCard |
| [Production repositories](Persistence/Repositories.g.cs) | Original BriefingCards.Create call, unchanged by fixture configuration |
| [Fixture/test doubles](Generated/TestDoubles.g.cs) | Explicit constructors, list initializer and materialized word array |
| [Manifest](Generated/manifest.json) | Exact used translations and ExplicitNestedDtoAndMaterializedCollectionConstruction policy |

HasInMemoryProjectionTranslation uses `allowNestedData: true` only for this registration.
Without that opt-in, projection-helper recipes remain flat. Child DTO types are checked recursively
against the final EF model; they do not need separate registrations just to construct them.
Services call CardsAsync/FindAsync/RenameAsync; no mapping DSL is exposed to the service.

The [consumer](Program.cs) references Contracts, Testing and DI, not EF, Models, Design or the
generator. It exercises fresh nested collections, visibility, pages, cold streams, version conflicts,
saved DTOs and baseline restore. Command translation permission remains independent.

Only independent public result data and explicitly materialized arrays/lists are supported.
Recursive or entity-bearing DTOs, captured/deferred collections and arbitrary graph mappers are
not enabled. Production helper calls still need normal provider translation outside a final
projection. See the [full guide](../../../docs/implemented/testing/INMEMORY-NESTED-PROJECTIONS.md).

Run: `dotnet run --project MemoryNestedProjections.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
