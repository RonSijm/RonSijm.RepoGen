# Membership: list and search inputs

List membership, reusable predicates, input policies and keyed-result collision policies.

## Start here

1. [MembershipDirectoryRepositoryDesigner.cs](Configuration/MembershipDirectoryRepositoryDesigner.cs) — what RepoGen generates.
2. [MembershipDirectoryContext.cs](Model/MembershipDirectoryContext.cs) — the EF context and entity model.
3. [MembershipContracts.cs](Contracts/MembershipContracts.cs) — handwritten application types and ports, not generated code.
4. [Program.cs](Program.cs) — setup, repository usage and executable assertions.
5. [Generated](Generated) — checked-in generator output; do not edit it manually.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs `MembershipDirectoryContext` with this designer:

```csharp
Generate(membershipDirectory, new MembershipDirectoryRepositoryDesigner().CreateModel(), "Membership",
    contracts: "Contracts/Generated/MembershipRepositories.g.cs");
```

| Generated file | What to find in it |
| --- | --- |
| [Contracts/Generated/MembershipRepositories.g.cs](../Contracts/Generated/MembershipRepositories.g.cs) | `IReadDirectoryItemRepository : IMembershipDirectory` and combined read-only `IDirectoryItemRepository` |
| [Generated/Repositories.g.cs](Generated/Repositories.g.cs) | Internal `DirectoryItemRepositoryOperations`, list normalization/guards and query/dictionary bodies |
| [Generated/manifest.json](Generated/manifest.json) | Required active-row policy, membership input policies and named read operations |

| Declaration/input | Specific generated effect |
| --- | --- |
| `WriteEnabled = false` / `BindReadPort<IMembershipDirectory>()` | Generated interface inherits the seven methods from the [handwritten IMembershipDirectory port](Contracts/MembershipContracts.cs); no write port |
| `HasVisibilityPolicy("Active", ...)` | Generated query roots require active rows independently of optional request filters |
| `HasQuery("FindByIds").WhereIn(...Distinct().MaximumCount(32))...List()` | `FindByIdsAsync(int[] ids, ...)` returns `IReadOnlyList<DirectoryItemSummary>` with generated bounded membership handling |
| `HasQuery("Search")...Use(minimumId)...WhereIn(...)` | `SearchAsync(DirectorySearch, ...)` applies declared null-value/input policies and the reusable minimum-ID predicate |
| `HasQuery("SearchText")...Use(...Not().And(...)).UseIf(...Or(...))` | `SearchTextAsync(DirectoryTextSearch, ...)` embeds the reviewed composite predicate; fragment tokens do not become runtime parameters |
| `HasQuery("CategoryCounts")...GroupBy(...)` / `HasQuery("CategoryCountsAtLeast")...UseIf(...)` | `CategoryCountsAsync(string[], ...)` / `CategoryCountsAtLeastAsync(DirectoryCountSearch, ...)` return grouped `DirectoryCategoryCount` lists |
| `Dictionary(...KeepFirst)` / `Dictionary(...KeepLast)` on ID-ordered rows | `FirstByCategoryAsync(...)` / `LastByCategoryAsync(...)` return `IReadOnlyDictionary<string, DirectoryItemSummary>` with those collision policies |

Open `Repositories.g.cs` and search for **`DirectoryItemRepositoryOperations`** to see the generated
methods. For the specific ID-list translation, search for **`FindByIdsAsyncCore`**. The public
contract inherits application-owned signatures rather than copying them; there is no separate
query-helper output or API snapshot here.

## Run

From the RepoGen repository root, using the checked-in generated code:

```powershell
dotnet run --project samples/ConfigurationExamples/Membership -c Release
```

After changing inputs, regenerate with `./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`
from the repository root. Running `Program.cs` does not run the designer or regenerate repositories.

## What this example demonstrates

The [designer](Configuration/MembershipDirectoryRepositoryDesigner.cs) defines seven named read methods:
ID lookup, request-bound list/text searches, a grouped category filter, grouped minimum counts and
first/last-per-category dictionaries.
Null IDs are rejected; empty ID/category lists return nothing. Only the search deliberately treats
null categories as an omitted constraint. Duplicate/null removal and raw-value bounds are opt-in
per filter and never change the caller's collection or bypass the required active-row policy.

Text matching combines approved name/category fragments with OR, composes a nonblank-name guard
with a request-bound minimum using NOT/AND, and omits text filtering when absent. A model-level DTO
fragment conditionally filters grouped counts. Local fragment request names deliberately differ;
`WithRequestName`/`UseIf` align them to the single application input. The original three operation
signatures are retained, and the required active-row policy remains independent of optional filters.

The [service](Program.cs) knows only the handwritten EF-free `IMembershipDirectory`
port. It has no `Contains`, EF method, normalization, or fluent query construction. The sample starts
with `UseSimpleDefaults()` and a borrowed context: no cache, tenancy, lease, unit of work, or provider
extension is required. In the original membership batch, all ten previous applications retained their generated outputs. See
[Membership queries](../../../docs/implemented/queries/MEMBERSHIP-QUERIES.md) for paging, streaming, caching, null annotations,
and explicitly bounded SQL Server/large-list behavior.

See [Reusable query fragments](../../../docs/implemented/queries/QUERY-FRAGMENTS.md) for composition, request binding,
scalar/DTO reuse and SQL translation boundaries. This batch intentionally changes only Membership's
generated persistence/manifest; the other eleven examples retain their generated outputs.

Optional [dictionary policies](../../../docs/implemented/queries/DICTIONARY-RESULTS.md) declare `KeepFirst` and `KeepLast`
over ID-ordered rows. Their port methods return DTO dictionaries directly; the service never groups
or keys rows. The program checks that the tools category selects IDs 1 and 2 respectively and that
the hidden ID 4 cannot become the last value. Existing dictionary calls still reject duplicate keys.

## Why these files are not compiled into the consumer

The files are kept together for reading, but the shared [Designers](../Designers/Designers.csproj),
[Models](../Models/Models.csproj) and [Contracts](../Contracts/Contracts.csproj) projects compile their
respective inputs. [ExampleSources.props](../ExampleSources.props) excludes those inputs from this
consumer's compilation. This preserves the design-time bootstrap and EF-free contract boundary;
the consumer does not reference the Designers project.

[All examples and suggested reading order](../README.md).
