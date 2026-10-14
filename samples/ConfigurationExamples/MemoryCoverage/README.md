# MemoryCoverage: explicit limits, never silent empty results

Start with [MemoryCoverageRepositoryDesigner](Configuration/MemoryCoverageRepositoryDesigner.cs).
It contains one portable read, two deliberately scripted reads and one scripted write. Strict
read generation is enabled; the designer explicitly acknowledges why the other reads need scripts.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs this designer with
[MemoryCoverageContext](Model/MemoryCoverageContext.cs) and emits four artifacts:

| Designer declaration | Exact generated result |
| --- | --- |
| Closed read/write capabilities | [IReadReviewedEntryRepository / IWriteReviewedEntryRepository / IReviewedEntryRepository](../Contracts/Generated/MemoryCoverageRepositories.g.cs) |
| Explicit store/copy adapter | [MemoryCoverageContextRepositoryFixture](Generated/TestDoubles.g.cs), with ReviewedEntryStore |
| `PortableList.IgnoreQueryFilters()` + RepoGen Visible policy | PortableListAsync uses the explicit designer visibility predicate; it does not silently drop an active EF filter |
| `EfFilteredCount.RequireTestOverride(reason)` | OnEfFilteredCountAsync script; invoking without arrangement throws UnsupportedInMemoryRepositoryOperationException with EF-filter/acknowledgement reasons |
| `WithReviewer.Select(...)` + result `RequireTestOverride(reason)` | OnWithReviewerAsync script; navigation loading is not emulated from a seeded graph |
| `HasCommand("Delete")` | OnDeleteAsync script; a returned affected count does not mutate fixture rows |
| `RequirePortableTestQueries()` | Removing a read acknowledgement produces RGD3302; it does not enable unsafe automatic execution |
| Real EF behavior | [Production implementation](Persistence/Repositories.g.cs), independent of the test-only acknowledgement |
| Coverage | [Manifest](Generated/manifest.json): PortableList Generated; other operations RequiresOverride with reasons |

The [handwritten inputs](Contracts/MemoryCoverageContracts.cs) are public POCO/DTO types. The
[consumer project](MemoryCoverage.csproj) excludes persistence/model/designer code and has no EF reference.

## What Program.cs proves

[Program.cs](Program.cs) verifies the portable visibility policy, checks the identity/reasons of
unsupported calls, arranges typed count/navigation/write results and resets the double. Reset
restores automatic PortableList behavior while unsupported operations still require scripts.
A scripted deletion result leaves both seeded rows intact.

The EF model filter and RepoGen policy happen to use the same scalar predicate here. The portable
query deliberately disables the former and retains the latter in *both* implementations. RepoGen
does not infer that arbitrary model filters are equivalent or import them into a fake.

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Check -Run -ValidateCompilation
dotnet run --project samples/ConfigurationExamples/MemoryCoverage -c Release
```

[MemoryCriteria](../MemoryCriteria/README.md) shows automatic reads that need no scripts.
See [portable fixture queries](../../../docs/implemented/testing/INMEMORY-QUERIES.md) for strict-mode and capability details.
