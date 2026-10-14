# Optional final-model filters for fixture commands

`command.UseInMemoryModelQueryFilter()` explicitly reuses a portable scalar filter from the final
EF model in that command's generated in-memory implementation. It is **off per command** and
independent of [the entity read opt-in](INMEMORY-MODEL-FILTERS.md). Production repositories, model
configuration, ports and API/cache shapes remain unchanged. Services still call named operations.

Start with [FilteredDraftsRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryModelWrites/Configuration/FilteredDraftsRepositoryDesigner.cs)
and its [exact designer → output map](../../../samples/ConfigurationExamples/MemoryModelWrites/README.md).

```csharp
// Existing EF model:
modelBuilder.Entity<Draft>().HasQueryFilter(draft => !draft.Archived);

// RepositoryDesigner, with fixture stores/write backend selected:
entity.HasCommand("Rename")
    .Where<RenameDraft>((draft, request) => draft.Id == request.Id)
    .Patch(setters => setters.SetProperty<string, RenameDraft>(draft => draft.Text,
        (draft, request) => request.Text))
    .UseInMemoryModelQueryFilter()
    .Save()
    .ReturnsSaved(draft => new DraftCard(draft.Id, draft.Text));
```

`UseInMemoryModelQueryFilter(false)` removes the command opt-in. No model filter or a disabled
command backend adds no source/receipt. Merely enabling reads does not enable these writes;
write filters can also run with the read backend disabled. Unsupported unarranged commands fail,
including on empty stores, rather than report false success.

## Where the filter applies

| Boundary | Filter behavior |
| --- | --- |
| Command selection | Filter saved rows before designer visibility/predicates; then use their tracked/pending values |
| Insert/CreateMany | Do not filter or validate insert candidates against a read predicate |
| Shared SaveChanges | Do not re-check the query filter; primary keys, original tokens and assigned-column merge rules still apply |
| Returns on a tracked command | Keep the declared affected-row mapping, not an additional saved-table query |
| ReturnsSaved | Reapply the model filter to fresh saved rows before designer result visibility and key lookup |

Thus staging an archive does not immediately remove the **saved** row from subsequent command
selection. Shared Save can persist that archive plus changes in unrelated repositories. A later
fresh result read may no longer find the row: `RepositorySavedResultException` occurs **after**
the shared save, not as an implicit undo. Saved batches never silently shorten their result list.
An explicit fixture checkpoint/real database transaction can roll back; do not blindly retry a
post-save result exception. See [shared write semantics](INMEMORY-WRITES.md) and
[fresh saved results](../writes/SAVED-COMMAND-RESULTS.md).

`BypassVisibility("Visible")` bypasses only that named designer policy. It does not bypass the
model filter. This slice adds no production command filter-bypass API or authorization inference.
Query filters are selection rules, not database constraints or an insertion permission system.

## Supported write subset

- Scalar tracked Create/Patch/TrackedDelete, Stage/shared Save and bounded CreateMany/PatchMany/
  leaf DeleteMany retain their existing permission, bounds, ordering, preparation and outcome rules.
- Scalar/DTO `ReturnsSaved` on eligible single/batch creates or patches uses a fresh filtered
  saved snapshot. [Filtered saved navigation DTOs](INMEMORY-SAVED-FILTERS.md) additionally require
  `includeNavigations: true` on this command and separate related-dataset graph permissions.
- Scalar immediate Update/Delete counts and native returning require **both** `SimulateImmediateWrites()`
  and this filter opt-in. Selection uses saved rows, keeps original-row setter semantics and does
  not flush or refresh pending writes. Zero rows remains a set-based `NotMatched`/zero count, not
  an invented distinction between missing and conflicting input.
  [Native result values](INMEMORY-NATIVE-FILTERS.md) use matched post-update/pre-delete rows, not a
  fresh filtered query; filter-changing mutations do not silently drop their own results.
- Both public entity-shaped stores and complete public read/write maps are supported. Internal
  persistence entities are not constructed in mapped fixture consumers.

The filter uses the same conservative [row/scalar inspection](INMEMORY-MODEL-FILTERS.md) as reads:
ordinary public mapped properties, a Boolean result and no captured contexts/services/collections,
clocks, ambient state, whole-entity behavior, shadow/field-only/unmapped/inherited shapes or
AutoInclude semantics. Existing copy/write-map/token/permission checks remain independent;
invalid incomplete write maps still fail their structural diagnostics.

[Scalar helper translations](INMEMORY-COMMAND-TRANSLATIONS.md) additionally require that command's
separate `UseInMemoryTranslations()` opt-in. Registering a read translation does not enable it for
writes. Replacement expressions still pass scalar/member/capture checks without invoking the
source helper or evaluating a context getter during generation.

Filtered graph reconciliation, unconfigured/unsupported saved navigation graphs and filtered atomic
insert/upsert remain explicit scripts/real-provider extensions. The per-command declaration does
not silently extend those backends or infer SQL constraints, collation, locking, races or cascades.

Strict `RequirePortableTestCommands()` raises `RGD3402` for uncovered operations unless
`RequireTestOverride(reason)` acknowledges a typed script. Successful `modelQueryFilter` operation
receipts describe selection, insert/no-fresh-query behavior, shared Save, saved results and limits.
Existing unconfigured examples remain byte-identical.

Shared contract cases compare actual generated SQLite repositories and fixtures across both store
shapes: filtered missing/stale outcomes, visibility bypasses, bounded batches, creates, immediate
counts, saved-row staging, cross-repository Save and explicit rollback after post-save result failure.
This is deliberate bounded fixture behavior, not proof of universal provider equivalence.
