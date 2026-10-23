# Saved command result queries

`ReturnsSaved` is an optional RepositoryDesigner choice for tracked `Create`, `Patch` or bounded
`CreateMany`/`PatchMany` commands that explicitly call `Save()`. It generates a fresh, untracked projection query **after**
the shared save succeeds. Services still call a named method; neither EF nor a projection DSL leaks
into the service contract.

Native `InsertIfAbsent`/`InsertOrUpdate` can separately use the same designer API. Their result
query runs **before** the operation's owned commit, without shared Save, and has different failure
semantics. See [atomic saved results](ATOMIC-SAVED-RESULTS.md); the lifecycle below describes the
tracked/shared-Save path only.

Set-based native Update instead uses the separately bounded
[ReturnsSavedMany mode](NATIVE-SAVED-RESULTS.md), with owned-transaction/borrowed-savepoint rollback
and no shared Save. It returns MutationResult rather than a tracked CommandResult.

```csharp
entity.HasCommand("Rename")
    .Where<RenameRequest>((row, request) => row.Id == request.Id)
    .Patch(setters => setters.SetProperty<string, RenameRequest>(row => row.Name, (row, request) => request.Name))
    .Save()
    .ReturnsSaved(row => new NotebookReceipt(row.Id, row.Name,
        row.Notes.Where(note => !note.Archived).OrderBy(note => note.Code)
            .Select(note => new NoteReceipt(note.Id, note.Code, note.Text)).ToList()));
```

Use `ReturnsSaved<TResult, TRequest>((row, request) => ...)` when a result also needs explicit
request values. Existing parameter binding, input/result nullability, navigation visibility review,
context selection/ownership and command timeouts apply. The result is `CommandResult<TResult>`,
and the normal application-port/CQRS contract remains EF-free. Bounded `CreateMany`/`PatchMany` instead returns
`CommandResult<IReadOnlyList<TResult>>`, using one result query and input-order correlation.
See [saved batch results](SAVED-BATCH-RESULTS.md) and the focused
[MemoryBatchResults](../../../samples/ConfigurationExamples/MemoryBatchResults/README.md) example.
The [CreateMany guide](CREATE-MANY-COMMANDS.md) and
[MemoryCreateBatches designer](../../../samples/ConfigurationExamples/MemoryCreateBatches/README.md) cover creation.

## Deliberate lifecycle

| Phase | Semantics |
| --- | --- |
| Selection and validation | Ordinary command predicates, visibility, expected versions and reconciliation rules apply |
| No match/conflict/no assignments | Return the existing outcome without querying a result |
| Save | Save the shared unit of work, including other repositories' staged changes |
| Result selection | Requery by **all saved primary-key properties**, not the mutation predicate, which may no longer match |
| Result visibility | Root designer policies apply unless deliberately bypassed; EF model filters remain active in production |
| Related data | Project related scalars/DTOs in the query; do not rely on tracked fixup, lazy loading or seeded navigation graphs |
| Result failure/cancellation | Throw `RepositorySavedResultException`; do not claim that the already-saved write was undone |

`Returns` still maps the command's saved entity directly. `ReturnsSaved` adds a separate read and its
cost only where explicitly chosen. It is not a SQL `OUTPUT`/`RETURNING` operation, snapshot-isolation
promise or automatic retry. Concurrent external changes between save and query may affect the result.
An explicitly surrounding transaction covers both operations using that context; isolation and
durability remain the provider's responsibility. A surrounding fixture checkpoint can restore both
tables after a saved result or result failure.

Root policies are rechecked; if a command makes its row invisible, the saved query finds no result
and reports a saved-result failure. Use deliberate `BypassVisibility` only when that result is intended.
Related designer policies are **not** silently injected into navigation traversal: filter related data
in the projection and use existing navigation-policy review to acknowledge access. Reconciliation
visibility and result visibility are separate, explicit decisions.

The generator rejects invalid lifecycle combinations with RGD3701: staged/deleted/set-based
Update commands, tracked commands missing `Save`, combining `ReloadAfterSave`, and keys lacking public readable CLR properties.
The last `Returns`/`ReturnsMany`/`ReturnsSaved` declaration selects the result mode. Database translation
of production projection expressions is still an EF/provider concern; there is no client fallback
for untranslatable query operations.

## Optional in-memory backend

The existing `UseInMemoryTestStores()` + `UseInMemoryTestCommands()` options can execute saved result
reads automatically. Scalar results need complete ordinary entity/public write maps. Navigation
results additionally require `UseInMemoryTestQueries()` and `HasInMemoryNavigation` on each traversed
relationship; mapped public stores need selected read slots at both ends. No new testing output or
dependency is introduced when the test backend is disabled.

After the atomic shared fixture save, the generated callback captures participating saved tables
under one lock, then loads detached relationship slots and evaluates the result outside the write
lock. Staged rows are not read directly. Explicit generated-value factories supply new saved IDs;
DTO lists/relationship slots never change the stored public arrangement rows. Same-partition context
selection, script precedence, baseline reset and checkpoint rollback retain their normal behavior.

This reuses the [navigation-read subset](../testing/INMEMORY-NAVIGATION.md) and
[public navigation maps](../testing/INMEMORY-PROXY-NAVIGATION.md): ordinary complete CLR key relationships,
explicit scalar/DTO projections and CLR structural equality. Required missing references, ambiguous
keys or result constructors can fail **after saving**. Unsupported filters/AutoInclude, inherited,
owned or implicit/shared joins, unguarded nullable-target SQL semantics, custom provider key comparisons, undeclared/unselected stores
or mapped entity identity require typed scripts/real-provider fixtures. The option does not expand
mutation selection/setters to navigation-dependent writes or broaden supported reconciliation graphs.

[Explicit required many-to-many join reads](../testing/INMEMORY-MANY-TO-MANY.md) are also available in saved
projections when all endpoint/join stores and accessed edges are declared. This adds a read after
saving, not graph writes or atomic returning; invalid relationship results still fail after the save.
[Nullable-link results](../testing/INMEMORY-OPTIONAL-LINKS.md) use the same directional source-FK/non-null-target
rules; both single and bounded batch saved DTOs are tested against actual generated SQLite operations.

`RequirePortableTestCommands()` reports unsupported results with RGD3402; `RequireTestOverride(reason)`
acknowledges scripts. Generated handlers remain overridable by the existing typed scripts. Supported
operations receive a `savedResultQuery` manifest receipt describing key, snapshot, visibility,
relationships and committed-result failure semantics. It makes no SQL constraint/cascade/race claim.

See [MemorySavedResults](../../../samples/ConfigurationExamples/MemorySavedResults/README.md): one focused
[SavedNotebookRepositoryDesigner](../../../samples/ConfigurationExamples/MemorySavedResults/Configuration/SavedNotebookRepositoryDesigner.cs)
maps directly to four generated artifacts and an independently EF-free executable. Shared tests also
execute actual generated SQLite and in-memory repositories against the same designer definitions.

Commands can separately [opt into registered fixture scalar-call translations](../testing/INMEMORY-COMMAND-TRANSLATIONS.md)
or [portable model filters in fresh saved graphs](../testing/INMEMORY-SAVED-FILTERS.md). Filtered ordinary
collections/guarded optional references need command-owned root permission and separate related
dataset permission, without enabling filtered reconciliation or changing post-save failure semantics.
Scalar-call translations are independently selected
for saved scalar/navigation DTO expressions and active root visibility. Production result queries,
relationship declarations, explicit related visibility and committed-result failure semantics stay unchanged.
