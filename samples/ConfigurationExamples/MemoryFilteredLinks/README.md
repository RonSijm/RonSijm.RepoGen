# MemoryFilteredLinks: filtered endpoints and join rows

Start with [FilteredCatalogRepositoryDesigner](Configuration/FilteredCatalogRepositoryDesigner.cs).
The [model](Model/FilteredCatalogContext.cs) declares archive filters on articles, labels and their
explicit CLR join table. Each filtered fixture participant opts in with
`UseInMemoryModelQueryFilter(includeNavigations: true)`. The designer declares stores, scalar maps
and the two accessed navigation directions; consumers call the named operations.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [Contracts](../Contracts/Generated/FilteredCatalogRepositories.g.cs) | Named Cards, Page, AllCards, Usage and Rename operations |
| [Production repositories](Persistence/Repositories.g.cs) | Original model-filtered EF navigation queries and scalar rename/save |
| [Fixture/test doubles](Generated/TestDoubles.g.cs) | Three public stores, filtered snapshots and typed LoadFilteredManyToMany calls |
| [Manifest](Generated/manifest.json) | Per-dataset filters, join semantics and separate saved-result permission |

| Designer choice | Result |
| --- | --- |
| Public seed maps on all three entities | Internal entities/read-only navigation collections stay out of the consumer |
| Article composite key; join surrogate key | Relationship matching uses the complete EF foreign keys, not fixture dictionary identity |
| Graph filter permission on all three tables | Archived endpoints/links are excluded without deleting saved data |
| Label Public visibility policy | Applies to Usage's label root, not implicitly to an article's labels |
| AllCards.IgnoreQueryFilters | Bypasses model filters on every participating table |
| Rename command's own graph filter permission | A scalar save can return a fresh filtered many-to-many DTO |

A required target excluded by its model filter removes that link, not the article. A truly missing
required row still fails fixture validation. Duplicate principal keys and visible join pairs also
fail. Filtered-out join rows do not participate. These are explicit fixture validity rules, not a
claim that SQL validates every row during a read.

The [consumer](Program.cs) references Contracts, Testing and DI only. It checks endpoints/join
filters, an inverse projection, related visibility, paging, bypasses, a fresh saved DTO, detached
lists and baseline restore. No EF, model, generator or designer assembly is loaded by the app.

The [guide](../../../docs/implemented/testing/INMEMORY-FILTERED-LINKS.md) also covers optional
targets: they retain the existing explicit non-null-filter or null-slot policy. This permission
does not add implicit/shared joins, required ordinary-reference pruning, graph mutation,
AutoInclude or provider-specific expressions. It adds no switch or output to simple configurations.

Run: `dotnet run --project MemoryFilteredLinks.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
