# MemoryScopedOptional: optional references with required owner columns

Start with [ScopedReviewRepositoryDesigner](Configuration/ScopedReviewRepositoryDesigner.cs).
The [context](Model/ScopedReviewContext.cs) maps an optional reviewer through
(ReviewerRegion, PartitionId, ReviewerCode). PartitionId is also part of the required folder FK:
an absent reviewer must not make the folder's ownership nullable.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [Contracts](../Contracts/Generated/ScopedReviewRepositories.g.cs) | Application read/write ports and versioned DTOs; no EF/entities |
| [Production repositories](Persistence/Repositories.g.cs) | Add-only nullable-component checks, then owner-consistency validation before binding |
| [Fixture/test doubles](Generated/TestDoubles.g.cs) | The same checks over public rows with independent fixture permission |
| [Manifest](Generated/manifest.json) | Explicit presenceColumns versus shared ownership and immutable-match semantics |

The designer combines OptionalAdditionalReference and AllowSharedReferenceColumns. Only
ReviewerRegion/ReviewerCode decide absence: both null is absent; both present supplies the complete
reference with PartitionId. A partial independent key is rejected on Add. PartitionId must agree
with the selected folder even for absent references. On matched rows, all Add-only values are ignored.

The [consumer](Program.cs) references Contracts, Testing and DI only. It checks absent/present
creation, partial-null rejection, absent cross-owner rejection, immutable matches, fresh DTOs,
explicit version inputs, Stage/shared Save, unchanged other owners, rollback and baseline restore.

This does not infer tenancy, authorization, reference existence, endpoint updates or graph writes.
SQL FK constraints remain provider-owned. Preexisting partial-null data is not normalized by the
Add policy. See the [full guide](../../../docs/implemented/writes/OWNER-SHARED-OPTIONAL-REFERENCES.md).

Run: `dotnet run --project MemoryScopedOptional.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
