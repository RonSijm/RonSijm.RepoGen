# Visibility: navigation review and trusted reads

Root/global visibility, explicit navigation review and a filtered loading profile.

## Start here

1. [VisibilityRepositoryDesigner.cs](Configuration/VisibilityRepositoryDesigner.cs) — what RepoGen generates.
2. [VisibilityContext.cs](Model/VisibilityContext.cs) — the EF context and entity model.
3. [VisibilityContracts.cs](Contracts/VisibilityContracts.cs) — handwritten application types and ports, not generated code.
4. [Program.cs](Program.cs) — setup, repository usage and executable assertions.
5. [Generated](Generated) — checked-in generator output; do not edit it manually.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs `VisibilityContext` with this designer:

```csharp
Generate(visibility, new VisibilityRepositoryDesigner().CreateModel(), "Visibility",
    contracts: "Contracts/Generated/VisibilityRepositories.g.cs");
```

| Generated file | What to find in it |
| --- | --- |
| [Contracts/Generated/VisibilityRepositories.g.cs](../Contracts/Generated/VisibilityRepositories.g.cs) | `IReadShowcaseDocumentRepository : IReviewedLibrary` and combined read-only `IShowcaseDocumentRepository` |
| [Generated/Repositories.g.cs](Generated/Repositories.g.cs) | Internal `ShowcaseDocumentRepositoryOperations`, protected query roots, loading/projection bodies and trusted history implementation |
| [Generated/manifest.json](Generated/manifest.json) | Visibility policies, navigation-review receipts, load profile and per-query bypass declarations |

| Declaration/input | Specific generated effect |
| --- | --- |
| `RequireNavigationVisibilityReview()` / `.ReviewNavigationVisibility<T>(...)` | Generation validates review declarations and records receipts; it does not add a runtime authorization service |
| `ShowcaseOwner.RepositoryEnabled = false` / `ShowcaseComment.RepositoryEnabled = false` | Their policy metadata can participate in document operations, but neither gets its own repository |
| `ShowcaseDocument.WriteEnabled = false` / `BindReadPort<IReviewedLibrary>()` | Generated interface inherits the four [handwritten IReviewedLibrary methods](Contracts/VisibilityContracts.cs); no write port |
| `HasVisibilityPolicy("CurrentDocuments", document => document.Active)` | Document query roots retain the active-document constraint |
| `HasQuery("GetSummaries")...ProjectTo<ReviewedDocumentSummary>().List()` | `GetSummariesAsync(...)` returns ordered summary DTOs with the reviewed owner visibility |
| `HasQuery("GetDetails").UseLoadProfile("PublicDetail")...AsSplitQuery()` | `GetDetailsAsync(int id, ...)` returns `ReviewedDocumentDetails?` with explicitly filtered comments and reviewed owner loading |
| `HasQuery("GetTrustedHistory").IgnoreQueryFilters()` with its explicit review | `GetTrustedHistoryAsync(...)` deliberately bypasses EF global filters while keeping the configured root policy; callers receive no bypass switch |
| `HasQuery("GetWithoutOwners").From(...anti-join...).List()` | `GetWithoutOwnersAsync(...)` returns an `IReadOnlyList<int>` using independently policy-filtered source sets |

Open `Repositories.g.cs` and search for **`ShowcaseDocumentRepositoryOperations`** or
**`GetTrustedHistoryAsyncCore`** for the actual generated code. Review explanations belong to
the manifest; they are not generated runtime permission checks.

## Run

From the RepoGen repository root, using the checked-in generated code:

```powershell
dotnet run --project samples/ConfigurationExamples/Visibility -c Release
```

After changing inputs, regenerate with `./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`
from the repository root. Running `Program.cs` does not run the designer or regenerate repositories.

## What this example demonstrates

The [designer](Configuration/VisibilityRepositoryDesigner.cs) enables the optional navigation review
guard on top of a simple borrowed context. Its ordinary summary uses an EF global filter to protect
an optional owner reference; detail loading explicitly selects public comments. Receipts identify
each protected target/policy and explain that choice in the generated manifest. A trusted historical
operation overrides its inherited projection receipt and deliberately ignores the owner global
filter, while retaining the required current-document root policy.

The [application](Program.cs) calls only `IReviewedLibrary`; it contains no runtime
policy bypass, include selection, query expression or EF tracking control. A typed anti-join also
shows that independently filtered source sets need no navigation receipt. The guard validates
review declarations, not authorization or predicate equivalence. It is off for unconfigured users,
and adds no cache, tenant resolver, owned lease, unit of work or provider package requirement.

This batch also regenerates the existing stream helpers with cancellation checks at each yield
and completion boundary; contracts/default feature selections remain unchanged. See
[Navigation visibility](../../../docs/implemented/queries/NAVIGATION-VISIBILITY.md) for diagnostics, cache dependency
hardening, and the separately opt-in SQL Server query/loading fixtures.

## Why these files are not compiled into the consumer

The files are kept together for reading, but the shared [Designers](../Designers/Designers.csproj),
[Models](../Models/Models.csproj) and [Contracts](../Contracts/Contracts.csproj) projects compile their
respective inputs. [ExampleSources.props](../ExampleSources.props) excludes those inputs from this
consumer's compilation. This preserves the design-time bootstrap and EF-free contract boundary;
the consumer does not reference the Designers project.

[All examples and suggested reading order](../README.md).
