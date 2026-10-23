# MemoryContextFilters: a context-valued model filter without a fixture DbContext

Start with [TenantNotebookRepositoryDesigner](Configuration/TenantNotebookRepositoryDesigner.cs).
The [EF context](Model/TenantNotebookContext.cs) owns a tenant/soft-delete filter. The designer
explicitly binds its `TenantId` member to a public [TenantPartition](Contracts/TenantPartition.cs).
The consumer runs with contracts and fixture runtime only, not EF, Models or Designers.

## Designer → generated output

| Designer declaration | Generated behavior |
| --- | --- |
| `UseKeyedContextLeases<TenantPartition>()` and per-operation `UseContextKey(...)` | [Fixture](Generated/TestDoubles.g.cs) selects `fixture.For(partition)`; production selects an application-supplied keyed context lease |
| `HasInMemoryModelFilterContext(context => context.TenantId, partition => partition.TenantId)` | Exact captured-context member becomes a typed key value; neither expression is evaluated at generation |
| Entity `UseInMemoryModelQueryFilter()` | `CardsAsync` applies both tenant and soft-delete predicates before its DTO projection |
| Per-command `UseInMemoryModelQueryFilter()` | Tracked rename and immediate archive constrain saved selection with the same filter |
| `Save().ReturnsSaved(...)` | Rename's fresh saved DTO query reapplies the same context-key filter |
| `Stage()` alongside immediate writes | Pending title changes remain staged; archive changes saved state immediately; shared Save does not recheck the filter |
| `IgnoreQueryFilters()` | `UnfilteredAsync` deliberately bypasses the entire EF filter, including tenancy, but cannot switch fixture partitions |
| Bound read/write ports | [Generated contracts](../Contracts/Generated/TenantNotebookRepositories.g.cs) and [production operations](Persistence/Repositories.g.cs) retain EF-free named application methods |

[Program.cs](Program.cs) seeds two partitions, including wrong-tenant rows in each. It verifies
filtering, identical IDs in isolated stores, blocked writes, fresh DTOs, retained staged state,
shared saves and the explicit bypass. [manifest.json](Generated/manifest.json) records the used
member/key recipes under `ContextBindings`. No fixture context or captured getter is constructed
or called.

## What stays optional?

The binding alone enables nothing. Stores, portable reads/writes, model-filter permissions and
keyed context selection are separate settings. Omit them all for the normal simple generated
repository. Unbound context members remain unsupported rather than being read at generation time.
Required filtered ordinary references, implicit/shared joins and filtered graph mutations are not
enabled by a binding.
[MemoryFilteredLinks](../MemoryFilteredLinks/README.md) separately opts explicit CLR endpoint/join
datasets into graph filters; those filters can reuse the same typed-key binding.

The example deliberately exposes `UnfilteredAsync` to demonstrate bypass semantics; an
application must decide who may use such an operation. Fixture partition keys must include all
identity/visibility values needed by the test. The production lease provider remains responsible
for choosing and configuring the real context.

## Generate and run

From the repository root:

```powershell
pwsh -NoProfile -File samples/ConfigurationExamples/Generate.ps1 -Configuration Release -ValidateCompilation -Run
dotnet run --project samples/ConfigurationExamples/MemoryContextFilters/MemoryContextFilters.csproj -c Release --no-build
```

See [the context-binding guide](../../../docs/implemented/testing/INMEMORY-MODEL-FILTER-CONTEXTS.md)
for the supported expressions and diagnostics.
