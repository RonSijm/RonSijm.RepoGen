# Optional filtered graphs in fresh saved DTOs

`command.UseInMemoryModelQueryFilter(includeNavigations: true)` additionally permits portable
scalar model filters in that command's ordinary navigation-based `ReturnsSaved` query. It is
**off per command**, independent of entity read flags and of graph mutation permissions. Existing
parameterless calls still cover scalar selection/fresh scalar results, not filtered saved graphs.
Production queries, contracts, API/cache shapes and unconfigured output are unchanged.

Start with [SavedShelfRepositoryDesigner](../../../samples/ConfigurationExamples/MemorySavedFilters/Configuration/SavedShelfRepositoryDesigner.cs)
and [its exact four outputs](../../../samples/ConfigurationExamples/MemorySavedFilters/README.md).
The consumer uses named ports, not a runtime EF/filter/projection DSL.

```csharp
entity.HasCommand("Rename")
    .Where<RenameRequest>((entry, input) => entry.Id == input.Id)
    .Patch(setters => setters.SetProperty<string, RenameRequest>(entry => entry.Title,
        (entry, input) => input.Title))
    .UseInMemoryModelQueryFilter(includeNavigations: true)
    .Save()
    .ReturnsSaved(entry => new EntryCard(entry.Id, entry.Title,
        entry.Shelf == null ? null : entry.Shelf.Id));

// The other filtered dataset needs its own explicit graph permission:
modelBuilder.Entity<Shelf>(related =>
    related.UseInMemoryModelQueryFilter(includeNavigations: true));
```

Stores/public scalar maps, explicit navigation declarations, `UseInMemoryTestCommands()` and
the navigation read backend remain required. The command owns its root dataset's permission,
even if root entity read filters are disabled. Any other filtered entity dataset needs the
entity-level graph opt-in. A self/cyclic use of the root dataset shares its command permission.
Related read permission alone never enables the command. Calling the parameterless command
method removes graph permission; `false` clears both. An unused permission, absent filters or a
disabled command backend adds no graph filter output/receipt. Scalar results need no read backend.

## Saving and reading are different boundaries

1. Scalar selection filters saved rows before designer root visibility/predicates. Existing
   tracked/pending values, bounds, keys, permissions and version/outcome rules remain unchanged.
2. The existing shared Save persists staged changes across repositories. Inserts are not
   validated against query filters, and Save does not re-check filters as constraints.
3. A fresh coordinated detached snapshot is mapped and each model filter runs once before
   declared relationships are loaded. Saved-result root visibility and complete-key correlation
   follow. A staged related-table archive can therefore change the DTO returned by another writer.

Collections omit model-filtered children, without injecting unrelated repository root visibility.
Optional filtered references become null; value access must satisfy the existing occurrence-local
[null-safety rules](INMEMORY-FILTERED-NAVIGATION.md). Single, bounded patch-batch and CreateMany
results retain input order and full key matching. Seeds/baselines and pending state are not deleted
by result filters. Related scalar helper translations still need `UseInMemoryTranslations()` on
this command; global registration or entity read settings alone do not enable them.

A root hidden by the post-save filter causes `RepositorySavedResultException` **after saving**.
A batch never silently shortens its results. No implicit undo/retry is added: use an explicit
transaction/checkpoint if the application requires rollback. See [saved-result failure semantics](../writes/SAVED-COMMAND-RESULTS.md).

## Boundaries retained

Required filtered references, filtered skip/many-to-many, context/navigation-based filters,
active AutoInclude, unsafe SQL null propagation and unmapped result/filter members require an
override. Filtered reconciliation, navigation-dependent selection/setters, arbitrary entity graph
persistence and native returning/upsert do not become portable through this read permission.
Unsupported acknowledged commands keep typed scripts; strict coverage reports `RGD3402`.

The `savedModelQueryFilters` receipt records predicates, separate permissions, post-save/pre-loading
order and committed-result failure scope. Execution remains LINQ-to-Objects, not provider parity.
Four SQLite contract cases compare actual generated repositories with both fixture store shapes,
including filtered collection counts, optional targets, shared saving, ordered batches, post-save
failures and explicit rollback.
