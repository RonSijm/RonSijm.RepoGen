# MemoryNullTargets: null-safe reporting over missing related rows

Start with [NullTargetNotebookRepositoryDesigner](Configuration/NullTargetNotebookRepositoryDesigner.cs).
The [context](Model/NullTargetNotebookContext.cs) has an explicit join with a required notebook FK
and optional label FK. The fixture preserves one missing-target slot per join only when configured.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/NullTargetNotebookRepositories.g.cs) | Handwritten read port and nullable string elements on NotebookSummary.Labels |
| [Production repositories](Persistence/Repositories.g.cs) | The original count/presence/conditional projection; no fixture policy injected |
| [Fixtures](Generated/TestDoubles.g.cs) | Coordinated three-store snapshot and preserveMissingTargets: true loader calls |
| [Manifest](Generated/manifest.json) | Per-edge missing-target/null-safety policy and no general SQL propagation/loading claim |

| Designer declaration | Generated effect |
| --- | --- |
| HasInMemoryNavigation(Labels, preserveMissingTargets: true) | Each absent label retains a null slot; two absent links count as two |
| HasProjection(NotebookSummary) | One design-time mapping reused by list/page/stream methods |
| Count / Count(label => label == null) | Total links and absent target count remain distinct |
| Conditional sort/projection | Null-safe scalar access; nullable output values are explicit |
| Root visibility | Hidden notebooks are omitted, not arbitrary related labels |
| Public scalar seeds/maps | Internal read-only model navigations are not constructed/assigned by the consumer |

The [consumer](Program.cs) checks repeated null targets, detached DTO mutation, a counted page,
cold snapshot-stable streams, fresh relationship reads and a cross-store baseline reset.
It references Contracts, Testing/Abstractions and DI, not EF, Models, Designers or generator tools.

Unlike [MemoryOptionalLinks](../MemoryOptionalLinks/README.md), which removes null targets using a
leading Where, this example deliberately observes them. The option stays off by default and cannot
authorize unsafe member access, entity identity, explicit Include/loading, SQL FK enforcement,
related authorization or many-to-many writes. Those limits remain in the generated capability receipt.

See [null-safe semantics and boundaries](../../../docs/implemented/testing/INMEMORY-NULL-TARGETS.md).
Run: `dotnet run --project MemoryNullTargets.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
