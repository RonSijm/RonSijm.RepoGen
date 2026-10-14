# Collections: explicitly configured child writes

One small context, one designer and two named write methods. This optional feature needs no cache,
resolver, lease factory or provider-specific writer. Start with the designer, not `Program.cs`.

## Designer → generated output

1. [ChecklistRepositoryDesigner](Configuration/ChecklistRepositoryDesigner.cs) declares the operations.
2. [CollectionsContext](Model/CollectionsContext.cs) supplies the relationship and unique `(ChecklistId, Key)` index.
3. [Request](Contracts/SaveChecklistItemsRequest.cs), [item input](Contracts/ChecklistItemInput.cs) and
   [saved view](Contracts/ChecklistView.cs) are handwritten EF-free application types.
4. [Program.cs](Program.cs) seeds SQLite and exercises the generated methods. It never runs the designer.

The [generation runner](../GenerationRunner/Program.cs) explicitly pairs these inputs:

```csharp
Generate(collections, new ChecklistRepositoryDesigner().CreateModel(), "Collections",
    contracts: "Contracts/Generated/CollectionsRepositories.g.cs");
```

| Output | What to find |
| --- | --- |
| [CollectionsRepositories.g.cs](../Contracts/Generated/CollectionsRepositories.g.cs) | `IWriteChecklistRepository.SaveItemsAsync` and `MergeItemsAsync`, plus its combined contract |
| [Repositories.g.cs](Generated/Repositories.g.cs) | `ChecklistRepositoryOperations`, input preflight, typed child mappings, scoped execution and DI registration |
| [manifest.json](Generated/manifest.json) | Each collection's key, limit, allowed assignments, missing-item policy and invalidation tag |

| Designer declaration | Specific output/behavior |
| --- | --- |
| `UseSimpleDefaults().UseClosedRepositories(...)` | Direct-context named methods, EF-free signatures, no service-side query/setter callbacks |
| `GenerateOnlyConfiguredEntities()` | Only the checklist repository is generated; its item entity needs no separate repository |
| `ReadEnabled = false` | This write-focused example emits no empty read interface |
| `Where(...ChecklistId...OwnerId...)` | Both writes select exactly one owner-scoped parent; a mismatched owner returns `NotFound` |
| `Patch(...Version + 1).ExpectVersion(...)` | A conditional parent write guards the cooperating aggregate writers; stale requests return `Conflict` |
| `ReconcileCollection(...Items...Key...)` | Existing children are queried through this selected parent's FK relationship |
| `.Add(...Key...Text...).Update(...Text...)` on `SaveItems` | New keys may be added and matched text updated; IDs, ownership and `InternalNote` are not request-writable |
| `.MissingItems(Delete)` on `SaveItems` | Missing visible items are explicitly deleted, not silently unlinked or inferred from a graph |
| No `Add`, no missing-item override on `MergeItems` | Unknown keys fail; missing members remain, because the default is `Keep` |
| `.MaximumItems(100)` | Duplicate/null/oversized inputs fail before context acquisition; oversized existing collections fail without writing |
| `.Save().Returns(...)` | `SaveItemsAsync` returns `CommandResult<ChecklistView>` containing saved items and the new parent version |

The generated contract is equivalent to:

```csharp
Task<CommandResult<ChecklistView>> SaveItemsAsync(
    SaveChecklistItemsRequest request, CancellationToken cancellationToken = default);
Task<CommandResult> MergeItemsAsync(
    SaveChecklistItemsRequest request, CancellationToken cancellationToken = default);
```

The application just calls `await writes.SaveItemsAsync(request, cancellationToken)`. It does not
reconcile keys, load children, specify EF setters or choose deletion behavior at runtime.

## Run

```powershell
dotnet run --project samples/ConfigurationExamples/Collections -c Release
```

The assertions cover add/update/delete, preserving an unconfigured field, retaining a missing merge
member, owner isolation and stale-version results. Regenerate after changing the designer:

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Run -ValidateCompilation
```

As in the other examples, shared [Models](../Models/Models.csproj), [Contracts](../Contracts/Contracts.csproj)
and [Designers](../Designers/Designers.csproj) compile the adjacent input files. The consumer imports
[ExampleSources.props](../ExampleSources.props) to exclude those inputs and references no designer.

## Boundaries

This is bounded tracked reconciliation of ordinary leaf children, not a bulk-import or automatic
business-rule mapper. The matching scalar must be unique globally or within the parent relationship;
new items need an application-supplied matching key (the database may still generate their separate ID).
Database/global filters and configured child visibility policies constrain which existing rows may
be changed. Read/result projections still need their own visibility design.

All aggregate writers must cooperate with the parent version convention. For versioned item-level
writes, the designer can also configure child `ExpectVersion`. Saved operations use the context's
shared `SaveChanges`, including unrelated pending changes. Staging is available explicitly through
`.Stage()` with a borrowed context/shared unit of work. See [collection reconciliation](../../../docs/implemented/writes/COLLECTION-RECONCILIATION.md)
for optional inputs, failure restoration, concurrency and unsupported graph shapes.

[All examples](../README.md).
