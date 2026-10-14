# RelationalContractFixture: one designer, one suite, two implementations

Start with [ContractNotesRepositoryDesigner](Configuration/ContractNotesRepositoryDesigner.cs).
Its five closed read operations produce both real EF implementations and automatic in-memory
fixture reads. [NoteReadContractCases](NoteReadContractCases.cs) defines the assertions once over
the handwritten [IContractNotes](Contracts/IContractNotes.cs) application port.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs this designer with
[ContractNotesContext](Model/ContractNotesContext.cs) and emits four artifacts:

| Declaration | Exact generated result |
| --- | --- |
| UseClosedRepositories + BindReadPort | [IReadContractNoteRepository : IContractNotes](../Contracts/Generated/ContractNotesRepositories.g.cs) |
| HasVisibilityPolicy + HasProjection | [Production operations](Generated/Repositories.g.cs), hiding Hidden rows and mapping reviewed DTOs |
| Find/Search/Page/Names/Stream | Named methods in the production implementation; no query configuration in services |
| Explicit store/copy + UseInMemoryTestQueries | [ContractNoteRepositoryTestDouble / shared fixture](Generated/TestDoubles.g.cs), with the same automatic read definitions |
| RequirePortableTestQueries | Non-portable reads need explicit acknowledgement at design time |
| Coverage receipts | [Manifest](Generated/manifest.json), Generated reads with detached .NET execution boundaries |

The public row/DTO/port inputs live under [Contracts](Contracts/ContractNote.cs). Context/model and
designer files remain independent generation inputs; consumer Program.cs does not execute a designer.

## What Program.cs proves

[Program.cs](Program.cs) runs five named cases against independently seeded automatic fixtures,
then runs the **same suite** against generated production repositories in real SQLite DI sessions.
It checks missing/hidden lookups, ordered search, counted pages/guards, keyed projection and cold
streams. Each relational case explicitly resets/reseeds its isolated database before opening a scope.

The optional real-provider harness owns the DI provider and factory-created opened SQLite connection.
No default reset/deletion policy is inferred. The consumer deliberately references EF/Models to run
the real implementation; the shared assertions only require application ports and EF-free Testing.
SQL Server-specific and non-portable semantics belong in separate integration cases.

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Check -Run -ValidateCompilation
dotnet run --project samples/ConfigurationExamples/RelationalContractFixture -c Release
```

See [contract suites and provider fixtures](../../../docs/implemented/testing/REPOSITORY-CONTRACT-FIXTURES.md) for
lifetime/error/reset policies. [MemoryCriteria](../MemoryCriteria/README.md) is a fully EF-free consumer;
[RelationalWrites](../RelationalWrites/README.md) tests real persistence and transactions separately.
