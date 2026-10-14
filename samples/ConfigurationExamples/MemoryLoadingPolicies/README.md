# MemoryLoadingPolicies: explicit loading despite model defaults

Start with [ExplicitLoadingRepositoryDesigner](Configuration/ExplicitLoadingRepositoryDesigner.cs).
The [context](Model/AutoLoadedCatalogContext.cs) configures automatic entry loading on an internal
group table. The designer chooses IgnoreAutoIncludes per operation and independently retains an
explicit detail profile. Public immutable seeds and handwritten ports keep the consumer free of
EF/models/designers/generation tooling.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [Contracts](../Contracts/Generated/AutoLoadedCatalogRepositories.g.cs) | Bound named reader; no EF/loading choice in its runtime API |
| [Production](Persistence/Repositories.g.cs) | IgnoreAutoIncludes on ordinary queries and both From roots; explicit detail Include remains |
| [Fixtures](Generated/TestDoubles.g.cs) | Detached mapped reads, shared relationship snapshot and no EF reference |
| [Manifest](Generated/manifest.json) | Per-query ignoreAutoIncludes true plus retained profile/path and read coverage |

| Designer declaration | Generated behavior |
| --- | --- |
| Cards + IgnoreAutoIncludes | Scalar read with Visible policy still applied |
| Detail + IgnoreAutoIncludes + Entries profile | Explicit related entries still selected and projected |
| Detail projection AutoIncludeNavigations(false) | Separately disables RepoGen's inferred includes, not EF model loading |
| Page's result builder IgnoreAutoIncludes | Same whole-query setting, not a DTO-only stage |
| Entries From + IgnoreAutoIncludes | Every Set root configured; root policies apply before joining |
| Stream | Cold detached snapshot; cancellation/early disposal unchanged |
| Public maps and HasInMemoryNavigation | Explicit read-row relationships, not construction of persistence entities |

The [consumer](Program.cs) checks visibility, details, caller mutation isolation, the two-root
operation, paging, stream snapshots and baseline restore. It uses only named port calls.

The option defaults to false. Removing it restores production model loading and the fixture's
honest override boundary. False can also remove a prior setting. Explicit loading, navigation
use in projections, query filters, owned loading and tracked fix-up are not erased. Read choices
do not opt commands into simulated writes.

See [loading-policy semantics](../../../docs/implemented/queries/EXPLICIT-LOADING-POLICIES.md),
[loading profiles](../../../docs/implemented/queries/LOADING-PROFILES.md) and
[navigation boundaries](../../../docs/implemented/testing/INMEMORY-NAVIGATION.md).

Run: `dotnet run --project MemoryLoadingPolicies.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
