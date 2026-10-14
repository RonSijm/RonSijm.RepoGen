# Repository designer source layout

[RepositoryDesigner.cs](RepositoryDesigner.cs) is the entry point: it creates a model builder,
invokes the consumer's `OnModelCreating` configuration and builds the design. The rest of the
design-time API is organized by responsibility, not bundled into that entry-point file.

| Folder | Responsibility | Start with |
| --- | --- | --- |
| `Builders` | Fluent configuration of models, entities, queries, commands, projections and options | [RepositoryModelBuilder](Builders/RepositoryModelBuilder.cs), [RepositoryEntityBuilder](Builders/RepositoryEntityBuilder.cs) |
| `Model` | Captured configuration, options and enums consumed by generation/validation | [RepositoryDesign](Model/RepositoryDesign.cs), [RepositoryEntityDesign](Model/RepositoryEntityDesign.cs), [RepositoryQueryDesign](Model/RepositoryQueryDesign.cs) |
| `Configuration` | Explicit configuration modules and provider declarations | [IRepositoryModelConfiguration](Configuration/IRepositoryModelConfiguration.cs), [IRepositoryEntityConfiguration](Configuration/IRepositoryEntityConfiguration.cs) |
| `Fragments` | Reusable predicate handles and their composition API | [RepositoryQueryFragment](Fragments/RepositoryQueryFragment.cs), [request-bound fragment](Fragments/RepositoryQueryFragment.Request.cs) |
| `Adapters` | Explicit model/contract mapping handles | [RepositoryTypeAdapter](Adapters/RepositoryTypeAdapter.cs) |
| `Loading` | Typed include chains, load-profile handles and internal loading resolution | [IRepositoryIncludeChain](Loading/IRepositoryIncludeChain.cs), [RepositoryLoadingConfiguration](Loading/RepositoryLoadingConfiguration.cs) |
| `Expressions` | Internal expression-tree inlining and parameter rebinding | [RepositoryAdapterExpressions](Expressions/RepositoryAdapterExpressions.cs), [RepositoryFragmentExpressions](Expressions/RepositoryFragmentExpressions.cs) |

Each C# file owns one top-level type. Related generic arities use `.Generic.cs` or `.Request.cs`
suffixes rather than sharing an unrelated type's file. Existing partial builders keep their
request-aware cursor and terminal operations in explicitly named files such as
[RepositoryResultQueryBuilder.RequestTerminals.cs](Builders/RepositoryResultQueryBuilder.RequestTerminals.cs).
Private nested helpers stay with the class that owns them.

Testing-only store choices have focused builder parts too: [key identity](Builders/RepositoryEntityBuilder.InMemoryKeys.cs)
is independent of [storage/read mapping](Builders/RepositoryEntityBuilder.TestDoubles.cs) and
[scalar writes](Builders/RepositoryEntityBuilder.InMemoryWrites.cs). Configuring fixture comparers
does not change production queries or require testing infrastructure in a simple setup.
[Immediate write simulation](Builders/RepositoryCommandBuilder.TestDoubles.cs) is separately opted
into on suitable Update/Delete/InsertIfAbsent/InsertOrUpdate commands; it changes no production API or simple defaults.
[Bounded child-write simulation](../../docs/implemented/testing/INMEMORY-COLLECTIONS.md) has its own per-command
`SimulateCollectionWrites()` opt-in, reusing production reconciliation rules rather than inferring graph persistence.
[ReturnsSaved](Builders/RepositoryCommandBuilder.SavedResults.cs) separately opts a saved Create/Patch
into a fresh complete-key result query, including explicitly projected related DTOs; see
[its lifecycle and fixture boundaries](../../docs/implemented/writes/SAVED-COMMAND-RESULTS.md).

All types remain in the `RonSijm.RepoGen.Design` namespace; folders express implementation
responsibilities, not extra imports required by consumers. See the
[configuration examples](../../samples/ConfigurationExamples/README.md) for designer-to-output walkthroughs.
