# Optional model filters in fixture child/link reconciliation

`command.SimulateCollectionWrites(includeModelQueryFilters: true)` permits portable scalar filters
from the final EF model on that command's explicitly selected child/link datasets. It is **off by
default**, separate from filtered reads, parent selection and saved-result graph permission.
It extends bounded tracked `ReconcileCollection`/`ReconcileLinks`, not arbitrary graph persistence.

Complete separately permitted [mapped scalar state](INMEMORY-MAPPED-COLLECTIONS.md) can supply
exact parent/child filter values too. Read bindings and write-state consent do not grant model-filter
write permission; the root and collection flags below remain independent.

Start with [FilteredChecklistRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryFilteredCollections/Configuration/FilteredChecklistRepositoryDesigner.cs)
and [its exact four outputs](../../../samples/ConfigurationExamples/MemoryFilteredCollections/README.md).
The EF/model/designer-free consumer calls named methods shared with production.

## Configure the permissions independently

```csharp
entity.HasCommand("ReplaceItems")
    .Where<ChangeItems>((parent, input) => parent.Id == input.Id)
    .ReconcileCollection<Item, ItemInput, string, ChangeItems>(
        parent => parent.Items, input => input.Items,
        item => item.Code, input => input.Code,
        collection => collection.MaximumItems(100)
            .Update(setters => setters.SetProperty(item => item.Text,
                (item, input) => input.Text))
            .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
    .UseInMemoryModelQueryFilter() // Separate parent-filter consent, if its model has a filter.
    .SimulateCollectionWrites(includeModelQueryFilters: true)
    .Save();
```

Both automatic fixture commands and selected writable parent/child stores remain required.
Public data shapes need complete scalar read/write maps and explicit generated-value adapters.
Existing ownership, matching-key, allowed-write, version, bounds, additional-reference and scalar
comparison rules still apply. No permission is inferred from a child repository's read settings.

The collection-write option covers only the datasets named by this command's reconciliation
rules. It does not require child read-filter permission and does not enable filtered result graphs.
Calling parameterless `SimulateCollectionWrites()` resets the additional filter permission to false;
`SimulateCollectionWrites(false)` disables simulation and clears the additional permission.
An absent filter, unused permission or disabled backend adds no filter source/receipt.
Simple/default generation remains unchanged.

## Selection, mutation and Save are different boundaries

| Phase | Behavior |
| --- | --- |
| Parent selection | Requires the command's own UseInMemoryModelQueryFilter when its model has a filter; designer predicates/visibility and version outcomes remain separate |
| Child/link candidates | Complete owner FK scope, then accepted model filter, then existing child designer visibility |
| Bounds and matching | Only surviving saved candidates count toward the existing-row limit, matching and missing-item Keep/Reject/Delete policies |
| New input key | A filtered match is absent from the candidate set; configured Add may create a new row, never fetch/update/move the hidden one |
| Creation | Not validated against model filters; owner columns are bound from the selected parent, with explicit factories using all saved/reserved identities |
| Update | Approved setters may make a matched row fail its filter; it is still staged/saved normally |
| Dirty targets | All owner-scoped added/modified/deleted pending rows still block reconciliation, including hidden/filtered rows |
| Shared Save | Persists approved staged work across repositories; does not reselect or recheck filters as constraints |
| Fresh result | ReturnsSaved has separate read/graph consent and post-save failure semantics |

Input presence/nulls/duplicates/limits, conditional `When`, expected versions, immutable ownership/
matching keys, command-only rollback before Save and explicit checkpoint rollback are unchanged.
An omitted conditional collection skips runtime selection, not generation-time capability checks.
Root `BypassVisibility` does not bypass model filters or the existing child-collection policies.
No IgnoreQueryFilters-style mutation bypass is introduced.

This matches the existing production path: it queries the selected parent's collection rather
than treating already tracked navigation members as the mutation scope. EF exposes
[collection queries](https://learn.microsoft.com/en-us/ef/core/querying/related-data/explicit#querying-related-entities)
and applies configured [model filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)
to normal queries. The fixture permission does not rewrite that production path.

## Saved DTOs and related endpoints stay separate

A filtered child/link write may return a scalar parent result without a read backend. A
navigation-based `ReturnsSaved` additionally needs the read backend, explicit navigation/store
maps and `command.UseInMemoryModelQueryFilter(includeNavigations: true)`, with entity graph
permission on related filtered datasets. A fresh snapshot is taken after the shared Save.

Child root visibility is a mutation policy, not an automatically injected projection policy.
The example deliberately shows a hidden, non-archived child in the saved DTO, while archived rows
are excluded by their model filter. Put additional reporting policy in the projection explicitly.
A missing/invalid fresh result fails after saving; use an explicit transaction/checkpoint if
rollback is required. See [filtered saved DTOs](INMEMORY-SAVED-FILTERS.md).

For `ReconcileLinks`, filtering the join dataset does not turn endpoint model filters into FK
constraints or authorization. Explicit scalar link creation can reference an existing filtered
endpoint; a separately filtered many-to-many saved projection can omit it. Related entities are
not created, updated or deleted. Shared/additional reference columns keep their existing guards.

## Portable fixtures are not constraint tests

Primary fixture-key collisions remain errors, and generated IDs include filtered saved rows.
SQL unique indexes, FKs, triggers, collations, rowversion, locks, races and durability are not
simulated. In particular, an input matching a filtered logical key is a possible addition: a real
unique index may reject it even when a fixture with a different primary key accepts it. Explicit
SQLite tests preserve this difference rather than claiming universal equivalence.

Filters must pass the existing scalar/member/capture checks. Unbound contexts, services/clocks,
navigation predicates, shadow/inherited/owned/shared state, active AutoInclude and unsupported
provider behavior retain overrides. [Exact context/key bindings](INMEMORY-MODEL-FILTER-CONTEXTS.md)
can separately supply scalar filter inputs; a child-only binding still resolves the operation key
once. [Scalar helper translations](INMEMORY-COMMAND-TRANSLATIONS.md) still need command-specific
translation permission; recipes are not evaluated during generation or substituted in production.

Strict coverage uses RGD3402; unsupported unarranged calls fail even on empty stores, and typed
scripts retain priority. Conditional `collectionSimulation.modelQueryFilters` receipts identify
accepted child predicates, separate parent consent, selection order, insertion/shared-Save and
dirty-target/result boundaries. Production/contracts/API/cache output is unchanged.

Focused tests cover owner/child filters, visibility, Keep/Reject/Delete, versions, Stage/shared
Save, filter-changing values, key bindings, recipes, strict guards, scripted overrides and unchanged
default output. Shared SQLite cases compare actual generated repositories with both fixture store
shapes for filtered reconciliation, saved DTOs, scoped link writes and hidden endpoints, and preserve
the deliberate unique-constraint difference. Unsupported filters retain explicit guard tests.
