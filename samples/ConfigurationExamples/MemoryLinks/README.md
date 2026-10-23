# MemoryLinks: write explicit join rows, not related label entities

Start with [BoardLabelsRepositoryDesigner](Configuration/BoardLabelsRepositoryDesigner.cs).
The [context](Model/BoardLabelsContext.cs) models Board ↔ Label through a real CLR join row with
caption/version payload. The designer writes the board's ordinary `Links` collection. Its separate
`Labels` skip navigation is used only for fresh saved/read DTOs. No related entity mapping is inferred.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/BoardLabelsRepositories.g.cs) | Named Find, MergeLabels, ReplaceLabels and StageLabels methods |
| [Production repositories](Persistence/Repositories.g.cs) | Bounded join-row queries, owner FK binding, scalar payload writes and shared-save guards |
| [Fixture and test doubles](Generated/TestDoubles.g.cs) | Public Board/Label/Link stores, mapped writes and the same reconciliation rules |
| [Manifest](Generated/manifest.json) | Complete owner/target FK roles, existing-reference-only receipt and explicit fixture constraint boundary |

| Declaration | Effect |
| --- | --- |
| `ReconcileLinks(board => board.Links, ...)` | Join rows are the mutation boundary; Labels/Boards are not inferred graph inputs |
| `link => link.LabelId` / `input => input.LabelId` | Complete target FK matching; ownership always comes from the selected board |
| `Add(input => new StoredBoardLabel { ... })` | Allow explicit linking to an existing label plus caption/version payload |
| `Update(...)` | Change only approved payload, never BoardId/LabelId |
| Merge + Keep versus Replace + Delete | Missing links are kept or deliberately removed; label entities survive |
| Parent and link `ExpectVersion` | Client aggregate/payload concurrency without stale-client retries |
| Stage versus Save + `ReturnsSaved` | Shared save boundary versus a fresh related DTO after saving |
| `SimulateCollectionWrites()` | Independent per-command opt-in to the existing fixture reconciliation engine |

The [consumer](Program.cs) references only Contracts, Abstractions, Testing and DI. It uses immutable
public seeds despite internal persistence types/read-only navigation accessors. It checks merge,
payload update, unlinking, other-owner isolation, unchanged labels, shared Stage/Save, saved DTOs,
checkpoint rollback and baseline restore. No service-side query/mapping extension methods are needed.

Linking requires exactly two required, disjoint FKs on an ordinary keyed leaf join entity and proven
owner/target pair uniqueness. Implicit property-bag joins, direct skip-navigation writes, optional
target links, overlapping/third FKs and nested graphs need explicit adapters. The production database
enforces target existence; the fake deliberately does not simulate FK/index constraints or SQL races.
All testing infrastructure remains off by default. See [full link semantics](../../../docs/implemented/writes/LINK-RECONCILIATION.md).

Run: `dotnet run --project MemoryLinks.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
