# Simple: the smallest designer

Inferred lookups and ordinary CRUD, with advanced helpers disabled.

## Start here

1. [SimpleRepositoryDesigner.cs](Configuration/SimpleRepositoryDesigner.cs) — what RepoGen generates.
2. [NotesContext.cs](Model/NotesContext.cs) — the EF context and entity model.
3. [Program.cs](Program.cs) — setup, repository usage and executable assertions.
4. [Generated](Generated) — checked-in generator output; do not edit it manually.

This example needs no handwritten DTO/port contracts. Its designer is deliberately just one preset;
advanced configuration is not required.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs `NotesContext` with this designer:

```csharp
Generate(notes, new SimpleRepositoryDesigner().CreateModel(), "Simple");
```

| Generated file | What to find in it |
| --- | --- |
| [Generated/Repositories.g.cs](Generated/Repositories.g.cs) | `INoteRepository`, internal `NoteRepository(NotesContext context)`, `NoteSelector` and `NoteCollectionSelector` |
| [Generated/manifest.json](Generated/manifest.json) | Fluent surface, borrowed context and disabled optional infrastructure |

| Declaration/input | Specific generated effect |
| --- | --- |
| `UseSimpleDefaults()` | Repository uses the supplied `NotesContext` directly; no resolver, cache decorator, lease or transaction wrapper |
| Model's `Note.Id` primary key | `NoteSelector ById(int id)` |
| Model's unique `Title` index | `NoteSelector ByTitle(string title)` |
| Writable keyed entity | `Add`, `AddAsync`, `SaveChanges`, `SaveChangesAsync`, plus selector write builders |
| No default sort configured | `GetAll()` and `GetAllUnordered()` introduce no `ORDER BY` |

For example, these are actual members of the emitted `INoteRepository` (excerpt; namespaces shortened):

```csharp
NoteCollectionSelector GetAll();
NoteSelector ById(int id);
NoteSelector ByTitle(string title);
void Add(Note entity);
Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
```

There is no separate query or EF-free contract output. The key/index accessors come from
[NotesContext's model](Model/NotesContext.cs), not hidden declarations in the one-line designer.

## Run

From the RepoGen repository root, using the checked-in generated code:

```powershell
dotnet run --project samples/ConfigurationExamples/Simple -c Release
```

After changing inputs, regenerate with `./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`
from the repository root. Running `Program.cs` does not run the designer or regenerate repositories.

## What this example demonstrates

[NotesContext](Model/NotesContext.cs) has a primary key and a unique title index. The
[minimal designer](Configuration/SimpleRepositoryDesigner.cs) selects only `UseSimpleDefaults()`. The tool generates `ById`, `ByTitle`, `GetAll`, and ordinary writes
from that EF model:

```csharp
var repository = new NoteRepository(context);
repository.Add(new Note { Title = "Try the simplest setup" });
await repository.SaveChangesAsync();
var note = await repository.ByTitle("Try the simplest setup").AsEntityAsync();
await repository.ById(note!.Id).Delete().SaveChangesAsync();
```

No DI extension, tenant resolver, cache provider, context lease, CQRS contract project, or
unit-of-work service is required. The context remains owned by the caller. The application verifies
save, unique lookup, collection listing, and fluent deletion.

Inspect the generated [repository](Generated/Repositories.g.cs) and
[manifest](Generated/manifest.json). All optional infrastructure helper switches are false
except synchronous helper methods; context lifetime is `borrowed`.

This is a simple setup, not per-method pruning of the selector runtime: common fluent read helpers
are still present. Existing no-preset defaults stay compatible; the runner selects this smaller
helper surface explicitly in C#. CLI `--profile simple` remains an equivalent convenience.

## Why these files are not compiled into the consumer

The files are kept together for reading, but the shared [Designers](../Designers/Designers.csproj),
[Models](../Models/Models.csproj) and [Contracts](../Contracts/Contracts.csproj) projects compile their
respective inputs. [ExampleSources.props](../ExampleSources.props) excludes those inputs from this
consumer's compilation. This preserves the design-time bootstrap and EF-free contract boundary;
the consumer does not reference the Designers project.

[All examples and suggested reading order](../README.md).
