# MemoryManyToMany: tags through an explicit join store

Start with [TaggedArticleRepositoryDesigner](Configuration/TaggedArticleRepositoryDesigner.cs).
The [EF model](Model/TaggedArticleContext.cs) defines a many-to-many relationship through a real CLR
join entity. Persistence classes are internal and their navigation collections are read-only.
The designer supplies public scalar fixture rows, maps both endpoints and the join, and explicitly
opts the accessed navigation edges into detached reads. No seeded navigation graph is needed.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/TaggedArticleRepositories.g.cs) | Named Cards, PublicCards, WithTag and Usage operations |
| [Production repositories](Persistence/Repositories.g.cs) | The original EF navigation queries/projections |
| [Fixture and test doubles](Generated/TestDoubles.g.cs) | Three public seed stores, plain read rows and typed LoadManyToMany calls |
| [Manifest](Generated/manifest.json) | Each operation's three participating stores, principal/join keys and only its accessed edges |

| Declaration | Effect |
| --- | --- |
| `HasInMemoryStore<ArticleRow, (int, int)>` | Different tenants can have the same local article ID |
| `MapInMemoryEntity` on all three tables | Scalar-only public arrangement; no persistence construction or navigation setters |
| `HasInMemoryNavigation(article => article.Tags)` | Load tags through the explicitly mapped join foreign keys |
| `HasInMemoryNavigation(tag => tag.Articles)` | Load the inverse only for operations that access it |
| `Cards` vs `PublicCards` | Related root policies are not injected; filtering hidden tags is explicit in the projection |
| `ArticleTag.Public` + `Usage` | The tag-root policy applies when querying tags as a root |
| No join query methods | A join can participate as a store-only table |

The [consumer](Program.cs) references Contracts, Testing and DI only—not Models, EF, Designers,
Generation or the production repositories. It verifies shared tags, complete composite joins,
empty collections, inverse projections, explicit visibility, detached lists and baseline restore.

This remains optional: it uses the existing `UseInMemoryTestQueries` and explicit store/navigation
declarations, with no new switch or service-side selection DSL. Required join foreign keys and
unique principal/link-pair keys are the supported fixture subset. Implicit/shared join entities,
unguarded nullable targets, unsupported provider policies and graph writes need an explicit override or
real-provider fixture. [MemoryFilteredLinks](../MemoryFilteredLinks/README.md) separately demonstrates
per-dataset model-filter permission on explicit CLR endpoints/joins and fresh saved DTOs.
[MemoryOptionalLinks](../MemoryOptionalLinks/README.md) separately demonstrates
the bounded nullable-link extension. See [many-to-many semantics](../../../docs/implemented/testing/INMEMORY-MANY-TO-MANY.md).

Run: `dotnet run --project MemoryManyToMany.csproj`.
Regenerate/check with the [shared example workflow](../README.md#generate-check-and-run).
