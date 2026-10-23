# Inventory: native returning writes

Set-based update/delete results through a closed write port, using a direct context.

## Start here

1. [InventoryRepositoryDesigner.cs](Configuration/InventoryRepositoryDesigner.cs) — what RepoGen generates.
2. [InventoryContext.cs](Model/InventoryContext.cs) — the EF context and entity model.
3. [InventoryContracts.cs](Contracts/InventoryContracts.cs) — handwritten application types and ports, not generated code.
4. [Program.cs](Program.cs) — setup, repository usage and executable assertions.
5. [Generated](Generated) — checked-in generator output; do not edit it manually.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs `InventoryContext` with this designer:

```csharp
Generate(inventory, new InventoryRepositoryDesigner().CreateModel(), "Inventory",
    contracts: "Contracts/Generated/InventoryRepositories.g.cs", apiSnapshot: true);
```

| Generated file | What to find in it |
| --- | --- |
| [Contracts/Generated/InventoryRepositories.g.cs](../Contracts/Generated/InventoryRepositories.g.cs) | `IReadInventoryItemRepository.ListStockAsync`, `IWriteInventoryItemRepository : IInventoryWrites` and combined `IInventoryItemRepository` |
| [Generated/Repositories.g.cs](Generated/Repositories.g.cs) | Internal `InventoryItemRepositoryOperations`, native returning-writer calls and DI registration |
| [Generated/manifest.json](Generated/manifest.json) / [RepositoryApi.json](Generated/RepositoryApi.json) | Command/result inventory and optional API/data candidate |

| Declaration/input | Specific generated effect |
| --- | --- |
| `UseSimpleDefaults().UseClosedRepositories(...)` | Named EF-free read/write contracts with a directly supplied context; no resolver or owned lease |
| `UseReturningWrites<SqliteReturningWriter>()` | Generated commands delegate native update/delete-returning execution to the selected optional provider writer |
| `BindWritePort<IInventoryWrites>()` | Write interface inherits the two methods from the [handwritten application port](Contracts/InventoryContracts.cs) |
| `HasQuery("ListStock")...Select(...InventoryChange).List()` | Read interface declares `ListStockAsync(...)` returning `IReadOnlyList<InventoryChange>`; no handwritten read port is bound |
| `HasCommand("AdjustStock")...Validate(...).Update(...).ReturnsMany(...)` | `AdjustStockAsync(AdjustStockRequest, ...)` returns `MutationResult<InventoryChange>` after the declared quantity/version update |
| `HasCommand("RemoveEmpty")...Delete().ReturnsMany(...)` | `RemoveEmptyAsync(...)` returns `MutationResult<InventoryChange>` containing deleted rows |

Open `Repositories.g.cs` and search for **`InventoryItemRepositoryOperations`**, then
**`AdjustStockAsync`**, to see the generated invocation of the writer. `SqliteReturningWriter`
itself is provider infrastructure, not another generated repository. Neither returning command
stages entities for a later `SaveChanges`.

## Run

From the RepoGen repository root, using the checked-in generated code:

```powershell
dotnet run --project samples/ConfigurationExamples/Inventory -c Release
```

After changing inputs, regenerate with `./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`
from the repository root. Running `Program.cs` does not run the designer or regenerate repositories.

## What this example demonstrates

[InventoryRepositoryDesigner](Configuration/InventoryRepositoryDesigner.cs) starts with the same simple
preset, then selects closed contracts and `UseReturningWrites<SqliteReturningWriter>()`. It uses a
plain scoped `InventoryContext`; there is no cache, context resolver, lease factory or unit of work.

`AdjustStockAsync(request)` changes a category's quantities and application-managed versions using
one native UPDATE statement and returns every changed `InventoryChange`. `RemoveEmptyAsync()` uses
native DELETE and returns deleted values. Both return `MutationResult<T>`: a copied/read-only item
list plus its affected-row count, with no guaranteed ordering. Neither loads rows before writing,
saves unrelated tracked state, or tracks its returned rows.

The [application-owned write port](Contracts/InventoryContracts.cs) is bound by the designer; the
sample service forwards requests without EF, setter expressions or `SaveChanges` calls. It checks
two updates, two deletes, an untouched category and an empty tracker. The generated API/data
snapshot is a candidate, not an automatically accepted compatibility baseline.

Register the selected native writer in infrastructure DI. SQL Server can instead select its
optional `SqlServerReturningWriter`; the core API and service shape stay the same. Exact transaction,
savepoint, conversion, caching and table-shape restrictions are in
[Returning writes](../../../docs/implemented/writes/RETURNING-WRITES.md). The other examples do not opt into this returning writer.

## Why these files are not compiled into the consumer

The files are kept together for reading, but the shared [Designers](../Designers/Designers.csproj),
[Models](../Models/Models.csproj) and [Contracts](../Contracts/Contracts.csproj) projects compile their
respective inputs. [ExampleSources.props](../ExampleSources.props) excludes those inputs from this
consumer's compilation. This preserves the design-time bootstrap and EF-free contract boundary;
the consumer does not reference the Designers project.

[All examples and suggested reading order](../README.md).
