# Configured: selected fluent extras

Accessors, named projections, whitelisted sorting, paging and optional cache/batch helpers.

## Start here

1. [CatalogRepositoryDesigner.cs](Configuration/CatalogRepositoryDesigner.cs) — what RepoGen generates.
2. [CatalogContext.cs](Model/CatalogContext.cs) — the EF context and entity model.
3. [CatalogContracts.cs](Contracts/CatalogContracts.cs) — handwritten application types, not generated code.
4. [Program.cs](Program.cs) — setup, repository usage and executable assertions.
5. [Generated](Generated) — checked-in generator output; do not edit it manually.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs `CatalogContext` with this designer:

```csharp
Generate(catalog, new CatalogRepositoryDesigner().CreateModel(), "Configured",
    projections: "Configured/Generated/Queries.g.cs");
```

| Generated file | What to find in it |
| --- | --- |
| [Generated/Repositories.g.cs](Generated/Repositories.g.cs) | `IProductRepository`, selectors, `ICacheableProductRepository` and internal `ProductRepositoryCacheDecorator` |
| [Generated/Queries.g.cs](Generated/Queries.g.cs) | Public `ProductConfiguredProjectionExtensions.ProjectToProductSummary` and `ProductConfiguredQueryExtensions.GetProductsAsync` |
| [Generated/manifest.json](Generated/manifest.json) | Enabled batch/cache helpers, tracking method names and the paged cached query |

| Declaration/input | Specific generated effect |
| --- | --- |
| `HasAccessor(product => product.Name)` | `ProductCollectionSelector ByName(string name)`; names need not be unique |
| `Exclude(product => product.Category)` | No automatic `ByCategory`; the configured query can still filter that EF column |
| `HasDefaultSort(...Name)` / `HasSortableFields(...Name, ...Amount)` | `GetAll()` orders by name; selector `OrderBy(string fieldName, bool descending)` accepts only the declared fields |
| `HasProjection(...ProductSummary)` / `ProjectionEnabled = false` | Named `ProjectToProductSummary(...)` helpers remain; arbitrary public projection helpers are omitted |
| `HasQuery("GetProducts")...AllowSorting(...).ProjectTo<ProductSummary>().Paged()` | `GetProductsAsync(category, sortBy, descending, page, pageSize, cancellationToken)` returns `PagedResult<ProductSummary>` as a repository extension method |
| `GenerateBatchOperations()` | Repository `AddRange`, `AddRangeAsync`, `UpdateRange` and `RemoveRange` |
| `GenerateCacheDecorator()` / query `.Cache(...)` | Optional cache wrapper and cached `GetProductsAsync` path; ordinary repository construction still needs only the context |
| `GenerateTrackingMethods().UseTrackingMethodNames(noTracking: "AsReadOnly", ...)` | `AsReadOnly()` on both product selector types, without public `AsTracking` / `AsNoTracking` controls |

Unlike the closed-operation examples, the named query here is a **generated extension method**,
not a declaration on `IProductRepository`. Its actual signature in `Queries.g.cs` is
(namespaces shortened):

```csharp
public static Task<PagedResult<ProductSummary>> GetProductsAsync(
    this IProductRepository repository, string category, string sortBy, bool descending,
    int page, int pageSize, CancellationToken cancellationToken = default);
```

Import the projection/query namespace to call it as `repository.GetProductsAsync(...)`, as
[Program.cs](Program.cs) does. `ProductSummary` itself is a handwritten [input contract](Contracts/CatalogContracts.cs).

## Run

From the RepoGen repository root, using the checked-in generated code:

```powershell
dotnet run --project samples/ConfigurationExamples/Configured -c Release
```

After changing inputs, regenerate with `./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`
from the repository root. Running `Program.cs` does not run the designer or regenerate repositories.

## What this example demonstrates

[CatalogRepositoryDesigner](Configuration/CatalogRepositoryDesigner.cs) starts with:

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .GenerateBatchOperations()
    .GenerateCacheDecorator()
    .GenerateTrackingMethods()
    .UseTrackingMethodNames(noTracking: "AsReadOnly", tracking: null,
        noTrackingWithIdentityResolution: null));
```

It adds a name accessor, suppresses the automatic category-index accessor, defines a default
sort and allowed sort fields, registers `ProductSummary`, and disables ad-hoc consumer projections.
The category remains available to a reviewed query; `Exclude` is not column removal.

The named query bundles the service's behavior at design time:

```csharp
entity.HasQuery("GetProducts")
    .Where<string>((product, category) => product.Category == category)
    .AllowSorting(product => product.Name, product => product.Amount)
    .AsReadOnly()
    .ProjectTo<ProductSummary>()
    .Cache(cache => cache.For(TimeSpan.FromMinutes(5)))
    .Paged();
```

The consumer calls `GetProductsAsync("drinks", "Name", false, 1, 10)` without rebuilding that
chain. Other sort field names are rejected by the generated whitelist. Paging executes a count
query and an item query, not a promise of a single SQL round trip.

[Program](Program.cs) stages a range, reads a sorted DTO page twice, checks the cache
hit, saves another product, and verifies invalidation. It also checks a neutral `AsReadOnly()`
entity lookup leaves the change tracker empty. It deliberately constructs the decorator;
an ordinary `ProductRepository(context)` does not require a cache provider. The
[sample memory-cache adapter](SampleMemoryCacheProvider.cs) is single-process demo
code, not a production/distributed consistency policy.

Inspect the generated [repository](Generated/Repositories.g.cs),
[named query/projections](Generated/Queries.g.cs), and
[manifest](Generated/manifest.json). Only caching and batch writes have been enabled
on top of the simple preset, along with the single neutrally named read control. Context resolution,
tracking-on/identity-resolution controls, retries, and transaction wrappers remain absent.

## Why these files are not compiled into the consumer

The files are kept together for reading, but the shared [Designers](../Designers/Designers.csproj),
[Models](../Models/Models.csproj) and [Contracts](../Contracts/Contracts.csproj) projects compile their
respective inputs. [ExampleSources.props](../ExampleSources.props) excludes those inputs from this
consumer's compilation. This preserves the design-time bootstrap and EF-free contract boundary;
the consumer does not reference the Designers project.

[All examples and suggested reading order](../README.md).
