# RonSijm.RepoGen

RepoGen generates typed repositories for Entity Framework Core.

Once several services need the same filter, projection and loading rules, you're keeping those
choices in several places. Add sorting, paging and tracking, and a simple read starts carrying
quite a bit of setup.

I want those choices in one `RepositoryDesigner`, so the service can just call the method it needs.

## What does that look like?

Given a `Pizza` entity and a `PizzaSummaryDto`, configure the repository like so:

```csharp
using RonSijm.RepoGen.Design;

public sealed class PizzaRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options
            .UseSimpleDefaults()
            .UseClosedRepositories("My.Application.Repositories")
            .GenerateOnlyConfiguredEntities());

        modelBuilder.Entity<Pizza>(entity =>
        {
            entity.HasProjection<PizzaSummaryDto>(pizza =>
                new PizzaSummaryDto(pizza.Id, pizza.Name));

            entity.HasQuery("FindByName")
                .Where<string>((pizza, prefix) => pizza.Name.StartsWith(prefix))
                .OrderBy(pizza => pizza.Name)
                .AsReadOnly()
                .ProjectTo<PizzaSummaryDto>()
                .List();
        });
    }
}
```

Then your service calls:

```csharp
var pizzas = await repository.FindByNameAsync("Ali", cancellationToken);
```

This does a couple of things:

- `HasProjection` defines the DTO mapping once.
  - `ProjectTo<PizzaSummaryDto>()` reuses that mapping in the named query.
- `FindByName` defines the filter, ordering and result together.
  - The service receives `FindByNameAsync`; it doesn't need to assemble that query.
- `UseClosedRepositories` generates separate read, write and combined interfaces.
  - The application contracts contain no EF types. Closed read operations default to no-tracking.
- `UseSimpleDefaults()` starts with direct-context CRUD and leaves out optional infrastructure helpers.
  - Caching, transactions, context leases and test backends are choices you enable when you need them.

See [generation options](docs/implemented/generation/GENERATION-OPTIONS.md) for the presets and
individual switches.

## Generate from C#

Put generation in a small runner project. Reference:

- `RonSijm.RepoGen.Generation`.
- Your model and designer projects.
- The EF provider used by the model.

Keep generated persistence out of the runner's dependencies. Otherwise the first generation
needs the code it is about to generate.

Run the engine like this:

```csharp
using RonSijm.RepoGen.Tool;

var bundle = RepositoryGenerator.Generate(context, new PizzaRepositoryDesigner());

var output = new RepositoryOutputLocations(
    "Persistence/Generated/Repositories.g.cs",
    Contracts: "Application/Generated/RepositoryContracts.g.cs",
    Manifest: "Persistence/Generated/manifest.json");

bundle.Write(output);
// bundle.Check(output); // Verify existing output without writing.
```

A few details:

- The engine reads the configured EF model. It does not create or connect to a database.
- Your application references the runtime packages; it does not need the runner or executable.
- You can write one file, split by entity, or give each contract its own file.

The [code-first generation guide](docs/implemented/generation/CODE-FIRST-GENERATION.md) covers the
project layout and CI checks. You can also enable
[compiler validation](docs/implemented/generation/COMPILATION-VALIDATION.md) to compile the generated
C# against your target references before writing files.

## Features

You don't need to turn everything on. The main options are:

- [Queries and projections](docs/implemented/queries/README.md).
  - Lookups from keys/indexes, custom filters, reusable fragments and named projections.
  - Joins, grouped reports, aggregates and dictionaries.
  - Whitelisted sorting, pages, slices, cursors and loading profiles.
- [Writes](docs/implemented/writes/README.md).
  - Typed create/patch/delete, staging, expected versions and bounded batches.
  - Collection/link reconciliation and fresh saved DTO results.
  - Native returning operations where the provider supports them.
- [Repository contracts](docs/implemented/repositories/README.md).
  - Combined repositories, CQRS selectors, or EF-free named operations.
  - Bind your own interfaces and configure tracking or type adapters.
  - Add context leases, units of work and caching when your setup needs them.
- [Testing](docs/implemented/testing/README.md).
  - Scripted doubles, shared stores, seeds and checkpoints.
  - Reuse supported designer queries/writes in fixtures, with explicit mappings for public row types.
  - Run shared contract tests against fixtures and a real provider.
- [Generation and validation](docs/implemented/generation/README.md).
  - Compiler checks, diagnostic IDs and API snapshots.
  - Split output, multiple contexts and checks that report stale files without rewriting them.

Each guide explains the supported shapes and where you need an override or a real database.

## Can I use the exe?

Yes. `repogen` runs the same engine as the C# runner. It builds the input projects, reads the
final EF model and applies your designer:

```powershell
repogen generate `
  --project path/to/My.Database.csproj `
  --designer-project path/to/My.RepositoryDesigner.csproj `
  --designer PizzaRepositoryDesigner `
  --generate-selectors `
  --output path/to/My.Persistence/Generated/Repositories.g.cs `
  --contracts-output path/to/My.Application/Generated/RepositoryContracts.g.cs
```

If keys and indexes are enough, you can omit the designer arguments. For more involved
configuration, put it in C# rather than trying to fit the whole design into command-line flags.

## Can I keep my own repository classes?

Yes. The attribute-based source generator gives you selectors and projection methods without
generating the whole repository.

Mark the entity and DTO:

```csharp
using RonSijm.RepoGen;

[GenerateSelectors]
public sealed class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;
}

[ProjectionFrom<Customer>]
public sealed record CustomerSummaryDto(Guid Id, string Name);
```

Return a generated selector from your repository:

```csharp
public CustomerSelector ByEmail(string email) =>
    new(context, context.Customers.Where(customer => customer.Email == email));

var summary = await repository.ByEmail(email).AsSummaryDtoAsync(cancellationToken);
```

- The projection runs before materialization, so EF selects the DTO columns.
- Matching DTO constructor parameters and properties are mapped at generation time.
  - If names aren't enough, supply an explicit projection expression.
- Reference `RonSijm.RepoGen`, `RonSijm.RepoGen.Generator` and `RonSijm.RepoGen.EntityFrameworkCore`.

The [source-generator sample](samples/RonSijm.RepoGen.Sample) shows the complete setup.

## Examples

The [AdventureWorks demo](examples/AdventureWorks/README.md) generates and compiles repositories
for all 71 tables in Microsoft's public sample database. Its pinned Git submodule supplies the SQL
schema; model scaffolding and repository generation run without a database connection.

There are 81 runnable [configuration examples](samples/ConfigurationExamples/README.md).
Start with an example's designer and README:

- The designer declares what gets generated.
- The README links those declarations to the actual generated files and methods.
- `Program.cs` is the consumer. It is not where you configure generation.

Useful starting points:

- [Simple](samples/ConfigurationExamples/Simple/README.md): minimal configuration.
- [Configured](samples/ConfigurationExamples/Configured/README.md): projections, sorting and paging.
- [Reports](samples/ConfigurationExamples/Reports/README.md): EF-free reporting methods.
- [Cqrs](samples/ConfigurationExamples/Cqrs/README.md): separate read/write selectors.
- [MemoryFixtures](samples/ConfigurationExamples/MemoryFixtures/README.md): fixture stores and seeds.
- [RelationalContractFixture](samples/ConfigurationExamples/RelationalContractFixture/README.md):
  the same interface tests against generated fixtures and real SQLite.

## A few things worth knowing

- Selectors stay lazy until a terminal method runs.
  - EF and your provider decide whether an expression translates. RepoGen does not silently
    switch to client-side filtering when it doesn't.
- `AsReadOnly` controls tracking. It doesn't make entities immutable or grant/revoke write permission.
  - You can configure the tracking method names or leave those methods out. Closed contracts keep
    tracking choices in the designer.
- `SaveChangesAsync` saves all tracked changes in the shared context.
  - That includes changes from other repositories. It is a unit-of-work save, not an entity-type save.
  - Staging doesn't persist anything; immediate commands execute when called.
- A counted page uses a count query and an item query. A slice skips the count.
  - Give paging a deterministic order.
- Native returning/upsert and temporal operations need the corresponding provider capability.
  - In-memory fixtures do not prove database constraints, SQL translation, race behavior or durability.
    Test those against a real provider.
- Selector-enabled entities must be top-level, non-generic classes.

## Packages

| Package | Purpose | Target |
| --- | --- | --- |
| `RonSijm.RepoGen` | Generation attributes | .NET Standard 2.0 |
| `RonSijm.RepoGen.Generator` | Incremental source generator | .NET Standard 2.0 analyzer |
| `RonSijm.RepoGen.Design` | RepositoryDesigner API | .NET Standard 2.1 |
| `RonSijm.RepoGen.Generation` | Referenceable code-first engine | .NET 10 design time |
| `RonSijm.RepoGen.Validation` | Optional C# compilation checks | .NET 10 design time |
| `RonSijm.RepoGen.Tool` | Standalone `repogen` executable | .NET 10 design time |
| `RonSijm.RepoGen.Abstractions` | EF-free results and unit-of-work contracts | .NET 8 |
| `RonSijm.RepoGen.EntityFrameworkCore` | Repository and selector runtime | .NET 8 |
| `RonSijm.RepoGen.Caching` | Optional scoped cache adapter | .NET 8 |
| `RonSijm.RepoGen.Testing` | Optional EF-free doubles and fixture runtime | .NET 8 |
| `RonSijm.RepoGen.Testing.EntityFrameworkCore` | Optional real-provider fixture infrastructure | .NET 8 |
| `RonSijm.RepoGen.Sqlite` | Optional native SQLite writers | .NET 8 |
| `RonSijm.RepoGen.SqlServer` | Optional SQL Server writers and temporal queries | .NET 8 |

## Build and test

Use the .NET `10.0.401` SDK pinned in `global.json`. Generated runtime and application projects can
target .NET 8.

Run:

```powershell
dotnet build RonSijm.RepoGen.slnx -c Release
dotnet test RonSijm.RepoGen.slnx -c Release --no-build -- xUnit.MaxParallelThreads=2
dotnet run --project samples/RonSijm.RepoGen.Sample
```

- SQL Server integration tests need `REPOGEN_SQLSERVER_TEST_CONNECTION`. Without it, those tests are skipped.
- The [example index](samples/ConfigurationExamples/README.md) has the generation, compilation and run commands.

For the detailed guides, see the [documentation index](docs/README.md).
