# Library: loading a detached graph

Reusable nested loading profiles, explicit nullability and a closed read port.

## Start here

1. [LibraryRepositoryDesigner.cs](Configuration/LibraryRepositoryDesigner.cs) — what RepoGen generates.
2. [LibraryContext.cs](Model/LibraryContext.cs) — the EF context and entity model.
3. [LibraryContracts.cs](Contracts/LibraryContracts.cs) — handwritten application types and ports, not generated code.
4. [Program.cs](Program.cs) — setup, repository usage and executable assertions.
5. [Generated](Generated) — checked-in generator output; do not edit it manually.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs `LibraryContext` with this designer:

```csharp
Generate(library, new LibraryRepositoryDesigner().CreateModel(), "Library",
    contracts: "Contracts/Generated/LibraryRepositories.g.cs", apiSnapshot: true);
```

| Generated file | What to find in it |
| --- | --- |
| [Contracts/Generated/LibraryRepositories.g.cs](../Contracts/Generated/LibraryRepositories.g.cs) | `IReadBookRepository : IBookLibrary` and combined read-only `IBookRepository` |
| [Generated/Repositories.g.cs](Generated/Repositories.g.cs) | Internal `BookRepositoryOperations`, nested loading/query implementation and DI registration |
| [Generated/manifest.json](Generated/manifest.json) / [RepositoryApi.json](Generated/RepositoryApi.json) | Loading/nullability inventory and optional API/data candidate |

| Declaration/input | Specific generated effect |
| --- | --- |
| `WriteEnabled = false` / `BindReadPort<IBookLibrary>()` | Generated read interface inherits the [handwritten IBookLibrary port](Contracts/LibraryContracts.cs); no `IWriteBookRepository` |
| `HasLoadProfile("Summary", ...)` / `HasLoadProfile<BookDetailsRequest>("Details", ...)` | Reusable design-time loading definitions; no runtime profile-selection parameter is added to the port |
| `HasProjection(book => LibraryMapping.Details(book)).AutoIncludeNavigations(false)` | Generated query uses the handwritten details mapping with explicitly configured loading, not guessed navigation loading |
| `HasQuery("GetBookDetails").UseLoadProfile(...).AsSplitQuery().ProjectTo<BookDetails>().SingleOrDefault()` | `GetBookDetailsAsync(BookDetailsRequest, ...)` returns `BookDetails?`; generated query loads author/address and request-filtered chapters/notes/reviews |
| `HasQuery("GetAuthorNames")...InputNullability(...).ResultNullability(...).List()` | `GetAuthorNamesAsync(IReadOnlyList<string?>? titles, ...)` returns `IReadOnlyList<string?>`; the configured predicate determines null/empty input behavior |
| `IncludeApiDataContracts()` plus runner `apiSnapshot: true` | `RepositoryApi.json` includes exposed request/result data shapes; the designer option alone does not choose an output file |

To see the generated loading code, open `Repositories.g.cs` and search for
**`GetBookDetailsAsyncCore`**. `LibraryMapping` and DTO definitions are handwritten inputs; RepoGen
generates their orchestration and the repository implementation, not another mapper class.
There is no public `Include` or `ProjectTo` chain on this closed port.

## Run

From the RepoGen repository root, using the checked-in generated code:

```powershell
dotnet run --project samples/ConfigurationExamples/Library -c Release
```

After changing inputs, regenerate with `./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`
from the repository root. Running `Program.cs` does not run the designer or regenerate repositories.

## What this example demonstrates

[LibraryRepositoryDesigner](Configuration/LibraryRepositoryDesigner.cs) registers two profiles:
`Summary` loads the optional author/address reference chain; `Details` filters chapters, their
nested notes, and reviews using one `BookDetailsRequest`. Collection continuations are typed,
including `ICollection<T>`, and note limits remain in the SQL loading query.

The named operation composes those profiles with the reviewed mapping:

```csharp
entity.HasQuery("GetBookDetails").UseLoadProfile(summary).UseLoadProfile("Details")
    .Where<BookDetailsRequest>((book, request) => book.Id == request.Id)
    .AsSplitQuery().RequireExplicitLoadStrategy()
    .ProjectTo<BookDetails>().SingleOrDefault();
```

The service uses only:

```csharp
var details = await applicationPort.GetBookDetailsAsync(
    new BookDetailsRequest(1, 2, 1, 4, false));
```

No service-side loading, tracking, or projection extensions are needed. Profiles contain paths,
not hidden tracking overrides. Closed read defaults leave the tracker empty; explicit split
loading makes the multiple-collection policy deliberate. No write contract or ad-hoc projection
is exposed.

[LibraryMapping](Model/LibraryContext.cs) deliberately converts the materialized graph
to manual DTO contracts. A pure SQL navigation projection would instead need its filters inside
the projection; filtered `Include` alone cannot define a projected collection's contents. See
[Loading profiles](../../../docs/implemented/queries/LOADING-PROFILES.md) for this distinction and provider limitations.

[Program](Program.cs) checks the filtered nested DTO, rebinding the second request, missing
books, and exactly-once disposal with no tracking. The resulting DTO remains usable after the
operation context is gone. The model/mapper references manual contracts, not generated persistence
or the designer. The EF-free [port](Contracts/LibraryContracts.cs) includes the graph read and a
`GetAuthorNamesAsync(IReadOnlyList<string?>? titles)` scalar read. Input/result nullability is
explicit in the designer: null titles select all books, empty titles select none, null elements
are accepted, and a book without an author returns a null item. All six read leases are checked.
See [Contract nullability](../../../docs/implemented/repositories/CONTRACT-NULLABILITY.md) for nested type declarations and limits.

The designer also opts into `IncludeApiDataContracts()`. The independent runner emits a version-2
[API candidate](Generated/RepositoryApi.json) that captures `BookDetailsRequest`, `BookDetails`
and nested `ChapterDetails` public data/constructor shapes. It adds no runtime dependency and
does not change the repository API or accept an approved baseline. See
[API compatibility](../../../docs/implemented/generation/API-COMPATIBILITY.md) for comparing a reviewed candidate; the Ledger
example retains its separate version-1 approved baseline. Simple/Configured/Reports do not acquire
snapshots or baseline requirements.

Inspect the generated [implementation](Generated/Repositories.g.cs),
[contract](../Contracts/Generated/LibraryRepositories.g.cs), and [manifest](Generated/manifest.json).
Neither registering a profile nor adding this example changes the simpler examples' query defaults.

## Why these files are not compiled into the consumer

The files are kept together for reading, but the shared [Designers](../Designers/Designers.csproj),
[Models](../Models/Models.csproj) and [Contracts](../Contracts/Contracts.csproj) projects compile their
respective inputs. [ExampleSources.props](../ExampleSources.props) excludes those inputs from this
consumer's compilation. This preserves the design-time bootstrap and EF-free contract boundary;
the consumer does not reference the Designers project.

[All examples and suggested reading order](../README.md).
