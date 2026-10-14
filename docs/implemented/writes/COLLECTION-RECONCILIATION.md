# Optional bounded child-collection reconciliation

`HasCommand(...).ReconcileCollection(...)` configures a closed tracked `Patch` command to match
ordinary one-to-many leaf children by a declared scalar or [composite matching key](COMPOSITE-COLLECTION-KEYS.md). It does not inspect arbitrary request
graphs or infer business rules. Unconfigured repositories retain their existing simple output.

See the focused [Collections designer](../../../samples/ConfigurationExamples/Collections/Configuration/ChecklistRepositoryDesigner.cs)
and its [declaration-to-output walkthrough](../../../samples/ConfigurationExamples/Collections/README.md).
Suitable leaf-child commands can separately opt into [in-memory child-store simulation](../testing/INMEMORY-COLLECTIONS.md)
with `SimulateCollectionWrites()`; it does not broaden these production graph boundaries.

## Configuration

```csharp
entity.HasCommand("SaveItems")
    .Where<SaveItemsRequest>((parent, request) =>
        parent.Id == request.Id && parent.OwnerId == request.OwnerId)
    .ReconcileCollection<Child, ChildInput, string, SaveItemsRequest>(
        parent => parent.Children,
        request => request.Items,
        child => child.Key,
        input => input.Key,
        children => children
            .MaximumItems(100)
            .Add(input => new Child { Key = input.Key, Text = input.Text })
            .Update(setters => setters.SetProperty(
                child => child.Text, (child, input) => input.Text))
            .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
    .Save();
```

The configured method accepts the application request, not entities or runtime EF expressions.
`ReconcileCollection` selects `Patch` when no command kind has been selected; it can be combined
with explicit scalar `Patch(...)` setters. Choose `Save()` or `Stage()` explicitly.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Add(mapping)` | Disabled | Unknown keys fail unless creation is explicitly mapped |
| `Update(setters)` | Disabled | Only declared scalars can be changed on matched children |
| `MissingItems(Keep)` | Keep | Unmentioned existing children remain |
| `MissingItems(Reject)` | Opt-in | Missing existing keys reject the request without writing |
| `MissingItems(Delete)` | Opt-in | Missing visible children are explicitly marked for deletion |
| `MaximumItems(n)` | 1,000 | Positive bound on supplied items and existing visible children |
| `When<TRequest>(condition)` | Always | False skips preparation, loading and mutation of that collection |
| `ExpectVersion(property, expected)` | Disabled | Bind a child's original concurrency value to the supplied item |
| `AllowWrite(property)` | Disabled | Explicit permission for protected scalar assignments such as an application-managed version; never permits changing ownership/matching keys |

Inputs are enumerated once into a bounded, keyed snapshot before acquiring a context. Null lists,
null items, null keys, duplicate CLR keys and excess items fail early. Values are not deep-cloned;
use immutable DTOs and do not concurrently mutate requests. Matching uses CLR default key equality;
database uniqueness/collation remains authoritative and may reject a save. There is no reflection-based
mapping or runtime field-name binding.

## Omitted versus empty

Use an `Optional<IReadOnlyList<ChildInput>?>` request property when the collection may be omitted:

```csharp
// In ReconcileCollection, bind request => request.Items.Value! as the item source.
children.When<PatchRequest>(request => request.Items.IsSpecified);
```

The condition must bind the same request type and parameter name as the item source. Omitted means
"do not touch this collection"; explicit null is rejected, not interpreted as delete-all. An empty
supplied collection follows the declared missing-item policy: keep, reject, or explicitly delete.
With no scalar assignments or active collection changes the command returns `NoChanges`.

## Ownership and visibility

The parent query is authored in the designer; authorization and selecting the correct parent are
application responsibilities. Root RepoGen visibility policies apply as on other tracked commands.
Children are queried through EF's relationship query, scoped to that parent's complete FK, with a
`maximum + 1` bound. New-child FK values come from the selected parent, never from the input mapping.
Neither updates nor creates may assign any child FK, and updates cannot change matching keys.

EF global filters and configured child RepoGen visibility policies are retained. Hidden rows already
present through change-tracker fixup are not added to the mutation scope. Missing-item policies act
on visible queried children, not every row in the underlying table. Unknown hidden keys are not
looked up through an unfiltered query or moved from another owner. An explicitly allowed addition
may still collide with a hidden/global database unique key and fail the save.

The optional navigation-review guard also sees the declared collection traversal. A review receipt
does not bypass filtering. Result projections do not become authorization-aware automatically:
already-tracked navigation members can include hidden rows; apply deliberate projection predicates
or use a separately configured read query for filtered results. Collection commands temporarily disable
context lazy loading across querying, mapping, saving and result construction, then restore the exact
prior setting on every exit. Implicit navigation getters cannot bypass the configured load bound.
`Returns` should use the already-loaded configured graph; additional navigation reads belong in an
explicitly designed query.

## Persistence, concurrency and failures

`Save()` uses one shared `SaveChangesAsync` call. That is not a promise of a single SQL statement or
network roundtrip, and unrelated pending changes in the same context participate. `Stage()` changes
only the tracker and is rejected for operation-owned leases; save later through a borrowed context
or shared unit of work. Multiple configured collections execute as one tracked command, with
earlier collection mutations restored if a later collection fails.

A saved command may use `Returns(...)` for its saved result, including generated child IDs.
Staged commands cannot return saved values. Collection commands do not support `ReloadAfterSave`:
reloading a scalar parent alone would not refresh the entire collection graph consistently.

For client-level aggregate concurrency, configure a parent `ExpectVersion` plus an unconditional
parent setter, typically incrementing an application-managed version. All aggregate writers must
cooperate with that convention. Child-only writes do not magically advance the parent's version.
Child `ExpectVersion` requires an unconditional child setter and validates supplied matched items;
the value must come from input, not from the just-loaded child. Missing-item deletions use the
loaded child's original EF token, not an absent client token. Protect stale full replacements with
the parent aggregate version. No stale-client retry is added.

Version mismatch/races on the parent or the command's children yield `Conflict`. Conflicts on
unrelated shared pending rows retain `RepositoryWriteConflictException`. Before-save errors and
failed saves restore command-assigned scalar values, child membership and states, detaching newly
created children without discarding unrelated pending edits. Existing pending changes in a target
collection are rejected rather than merged implicitly. A successful save followed by a result or
commit-observer failure is still committed; existing committed-failure exceptions remain in force.
Explicit transactions/unit-of-work, cancellation and post-commit cache invalidation still apply.
Child tags and dependent cached-query owners are added even when no child repository is generated.

## Supported shapes and diagnostics

Generation validates a direct principal one-to-many navigation to an ordinary keyed leaf entity,
a non-null mapped scalar or two-to-seven-component ValueTuple matching key, and an EF key/unfiltered unique index proving uniqueness
globally or within the owner's FK. Added items require a non-store-generated matching key; a separate
store-generated primary ID is fine. Scalar initializers and setters must be explicit and settable;
store-generated/key/ownership protections are validated as for other tracked mappings. At runtime the
navigation must be an initialized writable `ICollection<TChild>`; read-only collections fail before
setters or membership changes. Version guards require setters EF will actually save, not ignored columns.

`RGD2801` identifies unsupported collection/operation/key/limit/parent-version configuration.
`RGD2802` identifies invalid child mapping/version configuration. Other existing scalar-protection
and closed-contract diagnostics still apply.

Owned types, many-to-many/skip navigations, inherited/shared entities, nested child graphs,
cascading descendants, arbitrary/nested matching-key constructors, automatic unlinks/soft deletes and
provider-specific bulk reconciliation are not synthesized. Write an explicit adapter for such
shapes rather than weakening ownership or deletion guarantees. There is no new mandatory global
infrastructure setting or runtime query DSL.

For ordinary explicit two-FK join rows, [ReconcileLinks](LINK-RECONCILIATION.md) separately enables
complete target-FK matching and scalar reference/payload creation through the owner's join collection.
It never creates/deletes the related endpoint, changes existing FKs or enables direct skip-navigation
graph mutation. Ordinary ReconcileCollection retains its FK prohibition and original output.
The separate [shared-reference-column opt-in](SHARED-REFERENCE-COLUMNS.md) supports compatible
owner/target overlaps without changing ordinary collection matching or permitting owner transfer.
