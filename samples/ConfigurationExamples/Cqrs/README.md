# CQRS: read/write selector capabilities

Separate read/write selectors, per-entity capabilities and named command-side loads.

## Start here

1. [WorkboardRepositoryDesigner.cs](Configuration/WorkboardRepositoryDesigner.cs) — what RepoGen generates.
2. [WorkboardContext.cs](Model/WorkboardContext.cs) — the EF context and entity model.
3. [WorkboardContracts.cs](Contracts/WorkboardContracts.cs) — handwritten application types, not generated code.
4. [Program.cs](Program.cs) — setup, repository usage and executable assertions.
5. [Generated](Generated) — checked-in generator output; do not edit it manually.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs `WorkboardContext` with this designer:

```csharp
Generate(workboard, new WorkboardRepositoryDesigner().CreateModel(), "Cqrs",
    projections: "Cqrs/Generated/Queries.g.cs");
```

| Generated file | What to find in it |
| --- | --- |
| [Generated/Repositories.g.cs](Generated/Repositories.g.cs) | `IReadWorkItemRepository`, `IWriteWorkItemRepository`, their selector types, named command implementation and the other entities' ports |
| [Generated/Queries.g.cs](Generated/Queries.g.cs) | Named projections/query bodies; public CQRS methods in `Repositories.g.cs` forward to these internal extensions |
| [Generated/manifest.json](Generated/manifest.json) | Per-entity read/write capabilities, tracking and named operations |

| Declaration/input | Specific generated effect |
| --- | --- |
| `UseCqrsSelectors()` | Separate read and write interfaces; read selectors have no write builders and default to no-tracking |
| `HasProjection<WorkItemSummary>(...)` / `ProjectionEnabled = false` | `ProjectToWorkItemSummary(...)` on read selectors, without arbitrary consumer projections |
| `HasQuery("ListOpen")...ProjectTo<WorkItemSummary>().List()` | `IReadWorkItemRepository.ListOpenAsync(...)` returns `IReadOnlyList<WorkItemSummary>` |
| `HasQuery("LoadForUpdate").OnWriteRepository().AsTracking()` | `IWriteWorkItemRepository.LoadForUpdateAsync(int id, ...)` returns a tracked `WorkItem?`; absent from the read interface |
| `HasCommand("Complete")...Patch(...).Stage()` | `IWriteWorkItemRepository.CompleteAsync(int id, ...)` returns `CommandResult` and stages the patch; a later save persists it |
| `WorkLabel.WriteEnabled = false` / `WorkEvent.ReadEnabled = false` | Only `IReadWorkLabelRepository` / only `IWriteWorkEventRepository` respectively |
| Model's keyless `WorkSummary` | Only `IReadWorkSummaryRepository`; no write port is generated |

These named methods are real **interface members**, not service-side extension chains. Read/write
selector contracts here still reference EF model classes. There is no separate EF-free contract file;
that is the different output shape demonstrated by [Adapters](../Adapters/README.md).

## Run

From the RepoGen repository root, using the checked-in generated code:

```powershell
dotnet run --project samples/ConfigurationExamples/Cqrs -c Release
```

After changing inputs, regenerate with `./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`
from the repository root. Running `Program.cs` does not run the designer or regenerate repositories.

## What this example demonstrates

[WorkboardRepositoryDesigner](Configuration/WorkboardRepositoryDesigner.cs) adds only
`UseCqrsSelectors()` to the simple preset. `IReadWorkItemRepository` returns separate sealed read
selectors, not write-capable bases, and defaults to no-tracking. Tracking controls remain hidden.
Configured `ListOpenAsync()`, write-side `LoadForUpdateAsync(id)` and `CompleteAsync(id)` keep
the service's queries/mappings in code at design time; ad-hoc item projections are omitted from
the read selectors. `OnWriteRepository().AsTracking()` declares the command load explicitly;
read consumers cannot invoke it, and it does not imply a lock or save.

Labels are read-only, events are write-only, and a keyless workload view automatically has only a
read port. The generated registration shares the application's scoped context across ports.
The service stages the item patch, stages an event through another write repository, then saves
both through one shared `SaveChangesAsync()` call. It checks detached edits do not persist,
keyless results remain untracked, and the saved open-item list is correct.

There is no cache, resolver, owned lease, provider-specific writer or extra CQRS contract output.
Selector ports still reference model classes; use closed operations for EF-free application ports.
The [documentation](../../../docs/implemented/repositories/CQRS-SELECTORS.md) covers lifetimes, optional caching, named projection
enforcement and compile-time read/write isolation. Other examples do not gain CQRS selectors unless they configure them.

## Why these files are not compiled into the consumer

The files are kept together for reading, but the shared [Designers](../Designers/Designers.csproj),
[Models](../Models/Models.csproj) and [Contracts](../Contracts/Contracts.csproj) projects compile their
respective inputs. [ExampleSources.props](../ExampleSources.props) excludes those inputs from this
consumer's compilation. This preserves the design-time bootstrap and EF-free contract boundary;
the consumer does not reference the Designers project.

[All examples and suggested reading order](../README.md).
