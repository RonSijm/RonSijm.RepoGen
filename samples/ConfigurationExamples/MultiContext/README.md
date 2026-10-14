# Multi-context: independent stores

One generation batch for two contexts sharing an entity, with independent namespaces and saves.

## Start here

1. [PrimaryNoteRepositoryDesigner.cs](Configuration/PrimaryNoteRepositoryDesigner.cs) — what RepoGen generates. Also read [ArchiveNoteRepositoryDesigner.cs](Configuration/ArchiveNoteRepositoryDesigner.cs).
2. [NoteStores.cs](Model/NoteStores.cs) — the EF context and entity model.
3. [MultiContextContracts.cs](Contracts/MultiContextContracts.cs) — handwritten application types and ports, not generated code.
4. [Program.cs](Program.cs) — setup, repository usage and executable assertions.
5. [Generated](Generated) — checked-in generator output; do not edit it manually.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) explicitly pairs each designer with its context in
one `RepositoryGenerator.GenerateMany(...)` call. It selects `PerEntity` layout for primary and
`PerContract` for archive, so there is no shared `Repositories.g.cs`. Both use the same
handwritten `StoredNote` entity from [NoteStores.cs](Model/NoteStores.cs).

| Designer + context | Public generated API | Generated implementation |
| --- | --- | --- |
| [PrimaryNoteRepositoryDesigner](Configuration/PrimaryNoteRepositoryDesigner.cs) + `PrimaryNoteContext` | `IReadStoredNoteRepository` / `IWriteStoredNoteRepository` in [Primary.StoredNote.Repository.g.cs](Generated/RonSijm.RepoGen.ConfigurationExamples.MultiContext.Primary.StoredNote.Repository.g.cs) | Same file contains the primary repository/read wrapper; query bodies are in [Primary.StoredNote.Projections.g.cs](Generated/RonSijm.RepoGen.ConfigurationExamples.MultiContext.Primary.StoredNote.Projections.g.cs) |
| [ArchiveNoteRepositoryDesigner](Configuration/ArchiveNoteRepositoryDesigner.cs) + `ArchiveNoteContext` | [IReadStoredNoteRepository.g.cs](../Contracts/Generated/MultiContext/RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ArchiveNotes.IReadStoredNoteRepository.g.cs) inherits `INoteArchiveReader`; [IStoredNoteRepository.g.cs](../Contracts/Generated/MultiContext/RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ArchiveNotes.IStoredNoteRepository.g.cs) is its combined read-only facade | Internal `StoredNoteRepositoryOperations` and query bodies in [Archive.StoredNote.Repository.g.cs](Generated/RonSijm.RepoGen.ConfigurationExamples.MultiContext.Archive.StoredNote.Repository.g.cs) |

| Declaration/input | Specific generated effect |
| --- | --- |
| Primary `UseCqrsSelectors()` | Public primary read/write selector ports; `ById(int)` and ordinary writes are inferred from the model |
| Primary `HasQuery("ListActive")...ProjectTo<StoredNoteSummary>().List()` | `IReadStoredNoteRepository.ListActiveAsync(...)` returns ordered active-note DTOs |
| Archive `UseClosedRepositories(...)` / `WriteEnabled = false` / `BindReadPort<INoteArchiveReader>()` | Only closed read contracts; no archive write port or public selector |
| Archive `HasQuery("ListNotes")...ProjectTo<StoredNoteSummary>().List()` | Generated archive implementation supplies the handwritten port's `ListNotesAsync(...)` |
| Each designer's distinct `UseGeneratedNamespace(...)` | Primary and archive generated types are distinct despite their shared entity/type names; same ID does not select the other store |
| Runner's explicit output locations / layouts | Namespace-qualified filenames share the persistence folder safely; only archive has separate EF-free contract files and an API candidate |

Additional generated files, still attributable to one specific context:

- Primary [infrastructure/registration](Generated/RonSijm.RepoGen.ConfigurationExamples.MultiContext.Primary.PrimaryNoteContext.RepositoryInfrastructure.g.cs)
  and [Primary.manifest.json](Generated/Primary.manifest.json).
- Archive [infrastructure/registration](Generated/RonSijm.RepoGen.ConfigurationExamples.MultiContext.Archive.ArchiveNoteContext.RepositoryInfrastructure.g.cs),
  [Archive.manifest.json](Generated/Archive.manifest.json) and [Archive.RepositoryApi.json](Generated/Archive.RepositoryApi.json).

The archive's `INoteArchiveReader` and both stores' `StoredNoteSummary` are
[handwritten input contracts](Contracts/MultiContextContracts.cs), not another generated output.

## Run

From the RepoGen repository root, using the checked-in generated code:

```powershell
dotnet run --project samples/ConfigurationExamples/MultiContext -c Release
```

After changing inputs, regenerate with `./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`
from the repository root. Running `Program.cs` does not run the designer or regenerate repositories.

## What this example demonstrates

The [primary designer](Configuration/PrimaryNoteRepositoryDesigner.cs) and
[archive designer](Configuration/ArchiveNoteRepositoryDesigner.cs) deliberately reuse `StoredNote` in
two contexts. `UseGeneratedNamespace` separates their repository/selector/helper identities without
changing the model or DTOs. The primary context keeps simple CQRS reads/writes; the archive exposes
only an application-owned EF-free read port. Both start with ordinary borrowed scoped contexts.

The runner generates them as one `GenerateMany` batch, checks type/port/path collisions, and writes
split outputs into the same persistence directory. Archive contracts remain in their own generated
EF-free namespace. No cache, tenant resolution, owned leases or distributed transaction is required.
The service verifies distinct rows with the same key, then saves a primary write and confirms the
archive database was unaffected. C# aliases disambiguate same-named facade types where necessary.
See [Multi-context generation](../../../docs/implemented/generation/MULTI-CONTEXT-GENERATION.md) for ownership, caching and
batch guarantees. The other examples keep their own independent configuration.

## Why these files are not compiled into the consumer

The files are kept together for reading, but the shared [Designers](../Designers/Designers.csproj),
[Models](../Models/Models.csproj) and [Contracts](../Contracts/Contracts.csproj) projects compile their
respective inputs. [ExampleSources.props](../ExampleSources.props) excludes those inputs from this
consumer's compilation. This preserves the design-time bootstrap and EF-free contract boundary;
the consumer does not reference the Designers project.

[All examples and suggested reading order](../README.md).
