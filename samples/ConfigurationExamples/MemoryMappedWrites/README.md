# MemoryMappedWrites: explicit shadow-version fixture state

Start with [RevisionNotebookRepositoryDesigner](Configuration/RevisionNotebookRepositoryDesigner.cs).
The [one-table model](Model/RevisionNotebookContext.cs) has a shadow Revision concurrency token
computed from Counter. The designer supplies an exact public-data read binding, separately permits
fixture write state, and deliberately declares a matching generated-value recipe.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/RevisionNotebookRepositories.g.cs) | Bound read/write ports and public request/result types only |
| [Production operations](Persistence/Repositories.g.cs) | Original tracked expected-version checks, Stage/Save and fresh SQL projection |
| [Fixture implementation](Generated/TestDoubles.g.cs) | Typed scalar state, structural round-trip validation and explicit factory |
| [Manifest](Generated/manifest.json) | Separate mappedPropertyState permission and non-provider scope |

| Designer declaration | Generated effect |
| --- | --- |
| MapInMemoryProperty("Revision", ...) | Getter-free typed scalar read binding |
| MapInMemoryWrites(..., useMappedPropertyState: true) | Separate consent to include that value in complete scalar state/round trips |
| ExpectVersion("Revision", ...) | Explicit original-version check before mutation |
| HasInMemoryGeneratedValue("Revision", ...) | Deliberate fixture value during successful Save preparation |
| Stage versus Save / ReturnsSaved | Unchanged pending state/shared save, followed by a fresh saved DTO |

The [consumer](Program.cs) verifies stale rejection, Stage/shared Save, saved revisions and baseline
restoration without EF, entities or designers. Revision does not become a setter in the service API.
Nothing is inferred from the name "Revision" or from concurrency metadata alone.

The option is off by default. Keys, unique-index members and foreign keys still need ordinary
explicit assignments; this does not enable mapped graph writes, native returned buffers, EF tracker
state, database rowversion, SQL constraints or concurrency-race equivalence.

For separately opted-in native returned scalar values, see the focused
[MemoryMappedReturning designer](../MemoryMappedReturning/README.md). It composes complete state,
production capture and immediate simulation without inferring a provider buffer.

Run: `dotnet run --project MemoryMappedWrites.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
See [mapped fixture write state](../../../docs/implemented/testing/INMEMORY-MAPPED-WRITES.md).
