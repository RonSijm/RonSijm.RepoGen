# Adapters: application IDs and mappings

Closed application ports, typed IDs and reusable request/DTO mappings.

## Start here

1. [TaskDeskRepositoryDesigner.cs](Configuration/TaskDeskRepositoryDesigner.cs) — what RepoGen generates.
2. [TaskDeskContext.cs](Model/TaskDeskContext.cs) — the EF context and entity model.
3. [TaskDeskContracts.cs](Contracts/TaskDeskContracts.cs) — handwritten application types and ports, not generated code.
4. [Program.cs](Program.cs) — setup, repository usage and executable assertions.
5. [Generated](Generated) — checked-in generator output; do not edit it manually.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs `TaskDeskContext` with this designer:

```csharp
Generate(taskDesk, new TaskDeskRepositoryDesigner().CreateModel(), "Adapters",
    contracts: "Contracts/Generated/TaskDeskRepositories.g.cs", apiSnapshot: true);
```

| Generated file | What to find in it |
| --- | --- |
| [Contracts/Generated/TaskDeskRepositories.g.cs](../Contracts/Generated/TaskDeskRepositories.g.cs) | `IReadDeskTaskRepository : ITaskDeskReads`, `IWriteDeskTaskRepository : ITaskDeskWrites` and combined `IDeskTaskRepository` |
| [Generated/Repositories.g.cs](Generated/Repositories.g.cs) | Internal `DeskTaskRepositoryOperations` implements the ports; includes expanded adapter expressions, query/command bodies and DI registration |
| [Generated/manifest.json](Generated/manifest.json) / [RepositoryApi.json](Generated/RepositoryApi.json) | Operation inventory and optional API/data candidate |

| Declaration/input | Specific generated effect |
| --- | --- |
| `UseClosedRepositories(...)` | Public interfaces expose named operations, not selectors, EF entities or expression arguments |
| `BindReadPort<ITaskDeskReads>()` / `BindWritePort<ITaskDeskWrites>()` | Generated interfaces inherit these [handwritten ports](Contracts/TaskDeskContracts.cs); their methods are implemented, not redeclared |
| `HasTypeAdapter<int, TaskId>(...)` / `HasTypeAdapter<Guid, ProjectId>(...)` | `FindAsync(TaskId, ...)` and `ListByProjectAsync(ProjectId, ...)` use application IDs; generated predicates expand conversions to EF keys |
| `HasProjection(summary)` / `HasQuery("Find")...SingleOrDefault()` | `FindAsync` returns `TaskSummary?` through the declared shared mapping |
| `HasQuery("LoadForRename").OnWriteRepository()` | `LoadForRenameAsync(TaskId, ...)` exists on the write port and returns a DTO, not a tracked model |
| `HasQuery("ByBucket")...Lookup(...).KeyNullability(...)` | `ByBucketAsync` returns `IReadOnlyDictionary<TaskBucket<string?>, IReadOnlyList<TaskSummary>>` |
| `HasCommand("Create")...Save().Returns(...)` / `HasCommand("Rename")...Save().Returns(...)` | Generated `CreateAsync(CreateTask, ...)` / `RenameAsync(RenameTask, ...)` validate/apply/persist their designed operations and return `CommandResult<TaskSummary>` |

The short generated contract file is intentional: the method signatures live in the handwritten
ports it inherits. To see the generated implementation rather than those inputs, open
`Repositories.g.cs` and search for **`DeskTaskRepositoryOperations`**. The runtime consumer does not
call a mapper or need the Designers assembly.

## Run

From the RepoGen repository root, using the checked-in generated code:

```powershell
dotnet run --project samples/ConfigurationExamples/Adapters -c Release
```

After changing inputs, regenerate with `./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`
from the repository root. Running `Program.cs` does not run the designer or regenerate repositories.

## What this example demonstrates

[TaskDeskRepositoryDesigner](Configuration/TaskDeskRepositoryDesigner.cs) uses closed operations with
the simple preset. It registers `int`/`TaskId` and `Guid`/`ProjectId` adapters, composes a reusable
task-summary projection and a separate create-request mapping, and binds handwritten read/write
ports. The service calls only named operations with application IDs/requests; there are no EF
expressions, mapper dependencies or tracking switches in it.

The sample verifies persisted creates/renames, adapted database-generated IDs, project filtering,
an EF-free `LoadForRenameAsync` query on the handwritten write port, and a lookup keyed by
`TaskBucket<string?>`. The command read returns a DTO, not an editable EF entity. Its key object is never null, while the tag part may
be null; `KeyNullability` preserves this contract independently from the lookup's DTO values.
Its optional API/data snapshot records those application shapes without exposing adapter tokens.

## Why these files are not compiled into the consumer

The files are kept together for reading, but the shared [Designers](../Designers/Designers.csproj),
[Models](../Models/Models.csproj) and [Contracts](../Contracts/Contracts.csproj) projects compile their
respective inputs. [ExampleSources.props](../ExampleSources.props) excludes those inputs from this
consumer's compilation. This preserves the design-time bootstrap and EF-free contract boundary;
the consumer does not reference the Designers project.

[All examples and suggested reading order](../README.md).
