# Optional model-filtered native result simulations

Scalar `Update(...).ReturnsMany(...)` and `Delete().ReturnsMany(...)` fixture operations now
accept portable final-model query filters through the existing **two separate opt-ins**:
`SimulateImmediateWrites()` and command `UseInMemoryModelQueryFilter()`. No new switch is required.
Both remain off by default. Entity read permission does not enable either command option, and
automatic reads are not required for scalar mutation results. Production code, contracts,
API/cache shapes and unconfigured generated output remain unchanged.

Start with [NativeNotebookRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryNativeFilters/Configuration/NativeNotebookRepositoryDesigner.cs)
and [its exact four outputs](../../../samples/ConfigurationExamples/MemoryNativeFilters/README.md).

```csharp
entity.HasCommand("Archive")
    .Where<int>((note, id) => note.Id == id)
    .Update(setters => setters.SetProperty(note => note.Deleted, true))
    .ReturnsMany(note => new NoteCard(note.Id, note.Deleted))
    .SimulateImmediateWrites()
    .UseInMemoryModelQueryFilter();
```

Native result operations still need a configured production returning writer, public scalar
columns and permitted setters. A fixture acknowledgement cannot bypass production `RGD1306`
validation for shadow/backing-field state or navigation projections. Internal persistence models
need the existing complete public read/write maps; the consumer constructs no entity or DbContext.

## Selection and returned values

The model filter applies to **saved pre-mutation rows**, before designer visibility, predicates
and expected-version selection. `BypassVisibility` bypasses only a named designer policy, not the
model filter. All setters retain original-row semantics and explicit token/permission checks.

| Operation | Values returned | Filter after mutation? |
| --- | --- | --- |
| Update + ReturnsMany | Matched post-update scalar values | No |
| Delete + ReturnsMany | Matched pre-delete scalar values | No |
| Tracked command + ReturnsSaved | Fresh saved query after shared Save | Yes, separately designed |

An update setting `Deleted = true` therefore still returns its changed rows even though a fresh
repository read hides them. This is not `ReturnsSaved` and never implicitly re-queries/filter-prunes
the mutation result. Delete returns the matched values, not an empty result from the now-deleted
table. `AffectedRows == Items.Count`; zero items never guesses missing versus version mismatch.
Returned order remains unspecified.

Pending tracked changes are neither flushed nor refreshed. A native version change can make a
later shared Save conflict; unchanged tokens retain assigned-column merge behavior even when a
native update makes the saved row filtered out. Query filters are not Save constraints.
Existing fixture preparation completes copies, setters, factories, encoders and projections before
commit; failures/cancellation leave fixture saved/pending state unchanged. Explicit checkpoints
can roll back. See [immediate semantics](INMEMORY-IMMEDIATE-WRITES.md).

## Boundaries retained

Context/captured/ambient filters, unregistered helper methods, navigation predicates, inherited
or shadow state, active AutoInclude, graph results/mutations and provider-specific behavior stay
unsupported. Exact fixture helper translations need this command's own `UseInMemoryTranslations()`;
they do not insert a client-filter fallback into production SQL. Filtered atomic insert/upsert
remains a separate extension, not enabled by this slice.

Successful receipts distinguish matched post-update/pre-delete values from fresh filtered results.
Scripts still override execution; strict uncovered fixtures report `RGD3402`. Simulation makes no
SQL locking, trigger, constraint, ordering, race or universal provider-equivalence claim.
Four SQLite contract cases compare actual generated operations with both fixture store shapes,
including filter-changing updates, deleted values, bypasses, versions, pending conflicts,
unfiltered shared Save and explicit rollback.
