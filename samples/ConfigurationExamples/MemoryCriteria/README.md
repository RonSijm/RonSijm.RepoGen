# MemoryCriteria: the same designer reads, without a database

Start with [MemoryCatalogRepositoryDesigner](Configuration/MemoryCatalogRepositoryDesigner.cs).
It opts into automatic fixture reads; the service calls the same closed application port in
production and tests. Criteria, membership rules, visibility, sorting and projections stay in
the designer, not in consumer-side extension methods.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs this designer with
[MemoryCatalogContext](Model/MemoryCatalogContext.cs) and emits four artifacts:

| Designer declaration | Exact generated result |
| --- | --- |
| `UseClosedRepositories` + `BindReadPort<ICatalogReads>` | [IReadCatalogEntryRepository](../Contracts/Generated/MemoryCatalogRepositories.g.cs) inherits the handwritten application port |
| `HasInMemoryStore` + `UseInMemoryTestStores` | [MemoryCatalogContextRepositoryFixture / RepositoryData](Generated/TestDoubles.g.cs), with detached CatalogEntryStore rows |
| `UseInMemoryTestQueries` | [CatalogEntryRepositoryTestDouble](Generated/TestDoubles.g.cs) executes the four read definitions automatically |
| `UseKeyedContextLeases<CatalogPartition>` + query `UseContextKey` | Each read selects `Fixture.For(request.Partition)` from its designer binding |
| `HasVisibilityPolicy("Visible", ...)` | Every automatic read excludes Hidden rows |
| `HasQuery("Search")` | SearchAsync(criteria, page, pageSize): guard, optional text fragment, bounded/deduplicated IDs, safe field sort and counted page |
| `HasProjection` + `ProjectTo<CatalogItem>()` | Named mapping reused by Find/Search/Stream in production and fixtures |
| `HasQuery("Totals")` | Grouped Count/Sum report, with designer-defined ordering |
| `RequirePortableTestQueries()` | Generation fails if a read becomes non-portable without explicit acknowledgement |
| Same query definitions | [Production EF operations](Persistence/Repositories.g.cs), compiled/validated separately; no fake-only query DSL |
| Coverage inventory | [Manifest](Generated/manifest.json): each operation is Generated, with explicit LINQ-to-Objects/no-cache/no-tracking-simulation boundaries |

The public [POCO/criteria/DTO/port inputs](Contracts/MemoryCatalogContracts.cs) live in Contracts;
only the independent Model project references EF. The [consumer project](MemoryCriteria.csproj)
excludes persistence/model/designer code and references Contracts, Testing and DI.

## What Program.cs proves

[Program.cs](Program.cs) seeds two partitions with overlapping IDs, calls typed Find/Search/Totals,
verifies visibility and an invalid sort, overrides one generated read, resets its scripts, and
enumerates a cold stream. Partition baseline restoration never changes another partition.

No operation handler is needed for the ordinary reads. Automatic execution uses detached .NET
fixture semantics, not SQL translation/collation, EF global filters or real database transactions.
Do not mutate criteria while executing/enumerating; call recording snapshots are separate from
execution inputs. Real-provider tests remain necessary.

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Check -Run -ValidateCompilation
dotnet run --project samples/ConfigurationExamples/MemoryCriteria -c Release
```

Read [portable fixture queries](../../../docs/implemented/testing/INMEMORY-QUERIES.md) for the capability matrix.
[MemoryCoverage](../MemoryCoverage/README.md) shows explicit unsupported-read receipts and scripts;
[MemoryFixtures](../MemoryFixtures/README.md) keeps automatic reads disabled.
