# ScriptedPorts: service tests without EF

Start with [ScriptedNotesRepositoryDesigner](Configuration/ScriptedNotesRepositoryDesigner.cs).
It configures ordinary production queries/writes **and** explicitly opts into separate scripted test output.
[Program.cs](Program.cs) arranges and exercises those substitutes; it never executes the designer.
The executable references only Contracts, the optional Testing runtime and DI, not EF or Models.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs this designer with
[ScriptedNotesContext](Model/ScriptedNotesContext.cs) and writes three distinct C# outputs:

| Designer declaration | Generated result |
| --- | --- |
| `UseClosedRepositories(...)`, `BindReadPort<INoteLookup>()`, `BindWritePort<INoteWriter>()` | [Closed read/write/combined interfaces](../Contracts/Generated/ScriptedNotesRepositories.g.cs) inherit [handwritten service ports](Contracts/NoteContracts.cs) |
| Two `HasQuery("Find")` declarations, with `int` and `string` inputs | Production `FindAsync` overloads in [Persistence/Repositories.g.cs](Persistence/Repositories.g.cs); separate `OnFindAsyncByInt32` / `OnFindAsyncByString` scripts in [Generated/TestDoubles.g.cs](Generated/TestDoubles.g.cs) |
| `HasQuery("ListSelected")...List()` | `ListSelectedAsync(SelectedNotes, ...)` plus a typed `OnListSelectedAsync` script |
| `HasCommand("Create")...Save().Returns(...)` | Production `CreateAsync` returning `CommandResult<NoteSummary>`, plus a same-signature test operation |
| `UseScriptedTestDoubles(...)` | `IScriptedNoteRepositoryTestDouble`, `ScriptedNoteRepositoryTestDouble`, and explicit `AddScriptedNotesContextRepositoryTestDoubles()` registration in the test output |

`INoteLookup.ExistsAsync` has a handwritten default body. It is inherited, not replaced by another script.
The fixture supports generated CQRS/combined ports and both bound service ports using one scoped instance.
Calling test registration explicitly replaces production aliases for selected entities. Nothing activates it automatically.

## What the consumer demonstrates

- Separate overload scripts and argument matchers with explicit fallback results.
- A queued write failure followed by an explicitly arranged successful `CommandResult`.
- Bounded, opt-in call recording with an explicit adapter copying mutable request members.
- Reset and strict unarranged-call failures.

These scripts do **not** evaluate `Where`, apply sorting, assign database IDs, or save rows.
For example, `OnListSelectedAsync.Returns(...)` returns that exact arrangement regardless of the selected IDs.
This is a service-test substitute, not an in-memory database or proof of SQL translation.
Shared-state portable query/write execution remains a separate roadmap slice.

## Run and regenerate

```powershell
dotnet run --project samples/ConfigurationExamples/ScriptedPorts -c Release
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Run -ValidateCompilation
```

`Persistence/*.cs` is excluded from this consumer's compilation but checked by the runner.
The runner also independently compiles contracts and test doubles with EF-free references.
See [scripted test-double documentation](../../../docs/implemented/testing/SCRIPTED-TEST-DOUBLES.md) for configuration and limitations.
