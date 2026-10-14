# MemoryFixtures: shared typed test data, without a database

Start with [MemoryNotebookRepositoryDesigner](Configuration/MemoryNotebookRepositoryDesigner.cs).
It adds two explicit stores to the existing optional scripted test output. The fixture is shared
across repositories in one scope; seeds and inspected values are detached through designer-authored
copy adapters. Nothing is automatically enabled for ordinary users.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) explicitly pairs this designer with
[MemoryNotebookContext](Model/MemoryNotebookContext.cs) and writes these four artifacts:

| Designer declaration | Exact generated result |
| --- | --- |
| `UseClosedRepositories(...)` + `BindReadPort<IMemoryNoteLookup>()` | [Closed read/combined contracts](../Contracts/Generated/MemoryNotebookRepositories.g.cs); IReadMemoryNoteRepository inherits the handwritten lookup port |
| `UseScriptedTestDoubles(...)` | [MemoryNoteRepositoryTestDouble / MemoryLabelRepositoryTestDouble](Generated/TestDoubles.g.cs), typed OnFindAsync/OnCountAsync scripts and explicit scoped registration |
| `UseInMemoryTestStores()` | [MemoryNotebookContextRepositoryFixture / RepositoryData](Generated/TestDoubles.g.cs); every selected double receives the same fixture |
| `MemoryNote.HasInMemoryStore(...)` | Data.MemoryNoteStore with NoteKey and explicit nested Labels copying |
| `MemoryLabel.HasInMemoryStore(...)` | Data.MemoryLabelStore with a string key |
| `HasQuery("Find")` / `HasQuery("Count")` | [Production EF operations](Persistence/Repositories.g.cs); the test operations still require scripts |
| Storage configuration | [Manifest](Generated/manifest.json): entity shapes, adapters, scoped sharing, baseline scope and RequiresOverride operations |

The input [contracts/data shapes](Contracts/MemoryNotebookContracts.cs) are plain public .NET types,
compiled into the shared Contracts project. The EF context maps those types at design time. The
[consumer project](MemoryFixtures.csproj) excludes persistence, model and designer source and
references only Contracts, Testing and a DI container.

## What Program.cs proves

[Program.cs](Program.cs) resolves two doubles plus the bound application port, confirms their shared
fixture, seeds both stores, captures a baseline and edits original/inspected nested lists without
changing stored data. It arranges explicit read handlers, replaces/clears fixture rows, restores both
stores together and confirms another request scope starts empty. Script reset does not reset data.

This example intentionally first calls a seeded but **unarranged** query and verifies it fails.
The hand-authored test handler is not generated query lowering. No designer predicates, visibility,
write persistence, Stage/Save or transaction semantics are being simulated here.

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Check -Run -ValidateCompilation
dotnet run --project samples/ConfigurationExamples/MemoryFixtures -c Release
```

Read [in-memory fixtures](../../../docs/implemented/testing/INMEMORY-FIXTURES.md) for atomic range/baseline behavior,
copy requirements and opt-outs. [MemoryPartitions](../MemoryPartitions/README.md) adds typed
context selection and an explicit public storage proxy; neither is required for this simple fixture.
