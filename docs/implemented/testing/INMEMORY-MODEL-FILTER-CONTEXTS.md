# Explicit context values in fixture model filters

An EF filter can refer to an instance value such as `context.TenantId`. Portable fixtures have no
DbContext, so RepoGen cannot safely discover that value. Register a fixture-only binding to the
operation's typed context key instead:

```csharp
modelBuilder.Configure(options => options
    .UseKeyedContextLeases<TenantPartition>()
    .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands());

modelBuilder.HasInMemoryModelFilterContext<TenantNotebookContext, TenantPartition, string>(
    context => context.TenantId, partition => partition.TenantId);

modelBuilder.Entity<Note>(entity =>
{
    // HasInMemoryStore/MapInMemoryEntity and closed/test namespaces omitted here.
    entity.UseInMemoryModelQueryFilter();
    entity.HasQuery("Cards")
        .UseContextKey<NoteRequest, TenantPartition>(input => input.Partition)
        .Select(note => new NoteCard(note.Id, note.Title)).List();
});
```

The EF model still owns `note => note.TenantId == TenantId && !note.Deleted`. Neither registration
lambda nor the captured context getter runs during generation. The fixture handler evaluates the
operation's key selector once, uses that value for `Fixture.For(key)`, and closes the detached
filter over its declared scalar key values. Production expressions, contracts, cache identity and
API snapshots do not change.

Start with [TenantNotebookRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryContextFilters/Configuration/TenantNotebookRepositoryDesigner.cs)
and its [designer → output map](../../../samples/ConfigurationExamples/MemoryContextFilters/README.md).

## Permissions remain separate

- The global binding is off by omission. It does not enable stores, reads, writes or filters.
- Read roots still require entity `UseInMemoryModelQueryFilter()`. Every `From` dataset opts in
  independently. Bindings are shared recipes, not inherited dataset permissions.
- Each eligible tracked/batch or immediate scalar command still requires its own filter opt-in.
  A read permission never enables command filtering.
- Ordinary collection/locally guarded optional-reference reads and fresh saved DTO graphs still
  require their existing `includeNavigations: true` permissions and declared relationships.
- `IgnoreQueryFilters()` skips all model filters and their binding receipts for that operation.
  It bypasses a tenant filter too; this is not an authorization API. Designer visibility and
  fixture partition selection are independent.
- An absent model filter, unused binding or disabled backend adds no handler local or receipt.
  Configurations with no bindings retain their existing generated output.

## Supported shape

The source selects one exact public instance scalar property or field directly on the generated
context type (or its base context). There is no name matching, getter execution or reflection-based
value extraction. Matching requires that exact member on a captured context constant in the final
EF expression. Contexts hidden behind captured services/closures are not traversed.

The replacement has exactly the configured public `TContextKey` parameter and the same scalar
result type. Direct public scalar key slots and self-contained portable scalar expressions are
supported. A scalar key, such as `string`, can also be used directly. Register several members to
bind several filter inputs. Each exact source member has one binding across the designer/modules.

Captured variables/services/collections, clocks, custom helpers/operators, nested object graphs
and whole non-scalar key identity are rejected. Remaining unbound context values still require
a typed script or real-provider fixture; partial binding never silently drops a condition.
Existing mapped-scalar, null-guard, loading, translation and filter boundaries still apply.

## Write semantics and boundaries

Binding changes the filter input, not when filtering occurs:

- Tracked selection reads saved fixture rows. `Stage` retains pending assignments, and shared
  Save does not reselect or recheck the filter.
- Native-style immediate selection uses saved rows and leaves pending tracked state intact.
  Returned update/delete DTOs retain post-update/pre-delete semantics.
- `ReturnsSaved` reapplies the filter to fresh saved rows after the shared save. A missing
  filtered result can fail after persistence; it does not mean the write was rolled back.

Required ordinary references to filtered targets, implicit/shared joins and broader graph persistence
remain explicit override/provider boundaries. Bounded [filtered child/link reconciliation](INMEMORY-COLLECTION-FILTERS.md)
additionally needs its independent write opt-in; child-only bindings reuse the one resolved operation key.
[Explicit CLR many-to-many reads](INMEMORY-FILTERED-LINKS.md) separately reuse graph permissions;
bound endpoint/join filters use the same resolved operation key without loading a fixture context.
[Scalar atomic commands](INMEMORY-ATOMIC-FILTERS.md) can separately opt into filtered selection/
updated result checks; context binding alone enables no such operation.
This feature does not emulate SQL translation, collation, constraints, race or durability guarantees.
The application supplies real keyed leases and ensures key identity covers its database/visibility
scope; the generator does not interpret tenancy.

## Diagnostics and receipts

- `RGD4701`: invalid/direct source, duplicate member, wrong context/key type, missing keyed
  configuration, or non-scalar/hidden/entity key shape.
- `RGD4702`: a key-value recipe contains captured state or unsupported expression behavior.
- Existing `RGD3302`/`RGD3402` strict portability errors still apply to unbound filters and other
  unsupported read/write shapes.

Successful filter receipts contain only used `ContextBindings`: exact context/member, key/value
types and the scalar recipe. They accompany the existing application-order, bypass and
`capturedContext: NotEvaluated` receipts. Scripts keep priority and do not execute the automatic
key selector. Validation and cancellation precede automatic key resolution.
