# Accessor adapters: key and index conversion

Existing EF key conversion and explicit property adapters on read-only CQRS selectors.

## Start here

1. [LookupDirectoryRepositoryDesigner.cs](Configuration/LookupDirectoryRepositoryDesigner.cs) — what RepoGen generates.
2. [LookupDirectoryContext.cs](Model/LookupDirectoryContext.cs) — the EF context and entity model.
3. [DirectoryContracts.cs](Contracts/DirectoryContracts.cs) — handwritten application types, not generated code.
4. [Program.cs](Program.cs) — setup, repository usage and executable assertions.
5. [Generated](Generated) — checked-in generator output; do not edit it manually.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) makes this exact pairing:

```csharp
Generate(directory, new LookupDirectoryRepositoryDesigner().CreateModel(), "AccessorAdapters",
    projections: "AccessorAdapters/Generated/Queries.g.cs");
```

Here `directory` is a `LookupDirectoryContext`. `Generate` is the runner's local helper, which
sets these output paths and invokes the public `RepositoryGenerator` engine:

| Generated file | What to find in it |
| --- | --- |
| [Generated/Repositories.g.cs](Generated/Repositories.g.cs) | Public `IReadDirectoryEntryRepository`, sealed read selectors, internal implementation and `AddLookupDirectoryContextSelectorRepositories` DI registration |
| [Generated/Queries.g.cs](Generated/Queries.g.cs) | Implementations of `ProjectToDirectorySummary` and `ListEntriesAsync`; CQRS keeps these underlying extension classes internal |
| [Generated/manifest.json](Generated/manifest.json) | `cqrsSelectors`, read-only `DirectoryEntry`, no-tracking defaults and the `ListEntriesAsync` query |

The public repository interface in `Repositories.g.cs` is exactly this shape (namespaces shortened):

```csharp
public interface IReadDirectoryEntryRepository
{
    ReadDirectoryEntryCollectionSelector GetAll();
    ReadDirectoryEntryCollectionSelector GetAllUnordered();
    ReadDirectoryEntrySelector ByCodeAndId(DirectoryCode code, Guid id);
    ReadDirectoryEntrySelector ById(Guid id);
    ReadDirectoryEntryCollectionSelector ByCode(DirectoryCode code);
    Task<IReadOnlyList<DirectorySummary>> ListEntriesAsync(
        CancellationToken cancellationToken = default);
}
```

These are the individual input → output connections:

| Declaration/input | Specific generated effect |
| --- | --- |
| `UseCqrsSelectors()` | `IReadDirectoryEntryRepository` returns `ReadDirectoryEntrySelector` / `ReadDirectoryEntryCollectionSelector`; reads default to no-tracking |
| `entity.WriteEnabled = false` | No public `IWriteDirectoryEntryRepository` or write API on the read selectors |
| `HasAccessor(entry => entry.Id).UseValueConverter<Guid>()` | `ById(Guid id)`, instead of requiring the model's `StorageDirectoryId`; implementation reuses the EF converter |
| `HasAccessor(entry => entry.Code).UseAdapter(code)` | `ByCode(DirectoryCode code)` returns a collection selector, since code alone is not unique |
| EF model's unique `(Code, Id)` index, plus those accessor adaptations | `ByCodeAndId(DirectoryCode code, Guid id)` returns a single-entity selector; the composite index is declared in [the model](Model/LookupDirectoryContext.cs), not the designer |
| `HasProjection(entry => new DirectorySummary(...))` | `ProjectToDirectorySummary(...)` on both read selector types: a nullable DTO for a single lookup, a DTO list for a collection |
| `entity.ProjectionEnabled = false` | Read selectors omit arbitrary consumer-supplied projection methods; the named projection still exists |
| `HasQuery("ListEntries").OrderBy(...Name).ProjectTo<DirectorySummary>().List()` | `ListEntriesAsync(...)` with the declared name ordering and DTO mapping already inside the generated query |

Nothing creates a second DTO or requires a service-side projection. `DirectoryCode` and
`DirectorySummary` are handwritten [input contracts](Contracts/DirectoryContracts.cs).
The generated implementation uses them; it does not generate their definitions.

The corresponding consumer usage is in [Program.cs](Program.cs):

```csharp
var reads = scope.ServiceProvider.GetRequiredService<IReadDirectoryEntryRepository>();
var entry = await reads.ById(id).ProjectToDirectorySummary();
var entries = await reads.ListEntriesAsync(); // Ordered DTO list, no query chain in the service.
```

## Run

From the RepoGen repository root, using the checked-in generated code:

```powershell
dotnet run --project samples/ConfigurationExamples/AccessorAdapters -c Release
```

After changing inputs, regenerate with `./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`
from the repository root. Running `Program.cs` does not run the designer or regenerate repositories.

## What this example demonstrates

[LookupDirectoryRepositoryDesigner](Configuration/LookupDirectoryRepositoryDesigner.cs) configures this
read-only CQRS model. The ID already has an EF GUID converter; `UseValueConverter<Guid>()` reuses
its reverse expression for `ById(Guid)`. A separate `int`/`DirectoryCode` adapter affects only the
code property, including `ByCodeAndId(DirectoryCode, Guid)` inferred from the composite index.

Only configured DTO projections are exposed, and named `ListEntriesAsync()` retains detached
reads. The sample executes the single/composite lookups and checks that nothing was tracked.
It has no cache, tenancy, leases, runtime mapper or write port. See
[Type adapters](../../../docs/implemented/repositories/TYPE-ADAPTERS.md) for exact converter, nullability and expression limits.

## Why these files are not compiled into the consumer

The files are kept together for reading, but the shared [Designers](../Designers/Designers.csproj),
[Models](../Models/Models.csproj) and [Contracts](../Contracts/Contracts.csproj) projects compile their
respective inputs. [ExampleSources.props](../ExampleSources.props) excludes those inputs from this
consumer's compilation. This preserves the design-time bootstrap and EF-free contract boundary;
the consumer does not reference the Designers project.

[All examples and suggested reading order](../README.md).
