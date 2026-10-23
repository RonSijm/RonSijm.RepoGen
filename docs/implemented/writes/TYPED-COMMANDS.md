# Typed commands and concurrency

For explicitly supplied sets of keys, optional designer-defined [batch commands](BATCH-COMMANDS.md)
provide bounded PatchMany/DeleteMany, per-item original versions and a single shared Save or Stage.
For new rows, [CreateMany](CREATE-MANY-COMMANDS.md) provides bounded scalar mappings without
selection keys, all-candidate preparation and input-ordered IDs/DTOs through the same save choices.
See the focused [BatchWrites RepositoryDesigner example](../../../samples/ConfigurationExamples/BatchWrites/README.md).

These capabilities are optional designer declarations. Services pass application requests to named
write ports; they do not instantiate EF entities or choose arbitrary update expressions. Ordinary
selector CRUD remains available without any of this configuration.

## Create, save and return

```csharp
entity.HasCommand("Create")
    .Create<CreateRequest>(request => new Entry { Name = request.Name })
    .Save()
    .Returns(entry => new EntryResponse(entry.Id, entry.Name));
```

The generated method returns `Task<CommandResult<EntryResponse>>`. The mapping explicitly assigns
approved scalar properties. EF generates keys/computed values during the save; `ReloadAfterSave()`
can refresh additional store values before producing the result. `Returns` is a deliberate final
in-memory mapping, not a server-side projection or an implicit navigation load.

`Stage()` instead leaves the mapped change in the shared tracker. It returns `Staged`, not a saved
DTO, and cannot be used with `Returns` or `ReloadAfterSave`. A staged leased command requires a
borrowed context or shared unit of work: an operation-owned context would discard the staged work
when disposed. Choose `Save()` or `Stage()` explicitly for tracked commands.

The mapping validator rejects generated/immutable fields, arbitrary constructors, graph assignments
and unapproved foreign-key/concurrency assignments. Use `AllowWrite(entry => entry.OwnerId)` for an
intentional protected scalar assignment; it does not override EF's immutable/generated rules.
Audit/ownership conventions cannot reliably be inferred from names: omit those fields or explicitly
map them from a trusted input/interceptor. In closed repositories, optional
`entity.ProtectWrite(property)` requires local `AllowWrite` permission for ordinary scalars too;
see [write protection](WRITE-PROTECTION.md) and the focused
[WriteSafety designer](../../../samples/ConfigurationExamples/WriteSafety/README.md). This is field control,
not authorization.

## Partial patches, including explicit null

`Optional<T>` represents presence separately from value:

| Input | `IsSpecified` | Meaning |
| --- | --- | --- |
| `default(Optional<string?>)` / omitted JSON property | false | Leave the column alone |
| `new Optional<string?>(null)` / JSON null | true | Assign null |
| `new Optional<string?>("new text")` | true | Assign the supplied value |

Reading `Value` on an unspecified field throws. When serializing optional request properties, use
`JsonIgnoreCondition.WhenWritingDefault` to omit unspecified fields. The included converter handles
present values/null; it never silently serializes missing as null.

```csharp
entity.HasCommand("Patch")
    .Where<PatchRequest>((entry, request) => entry.Id == request.Id)
    .Patch(setters => setters.SetPropertyWhen<string?, PatchRequest>(
        entry => entry.Description, (entry, request) => request.Description.Value,
        request => request.Description.IsSpecified))
    .ExpectVersion<int, PatchRequest>(entry => entry.Version,
        (entry, request) => request.ExpectedVersion)
    .Save()
    .Returns(entry => new EntryResponse(entry.Id, entry.Name));
```

The executor fetches one tracked row using the configured predicates/visibility, assigns only the
declared properties, and uses the caller's expected version as the original concurrency value.
`ExpectVersion` requires an EF concurrency property and a request-bound expected value; it must
not use the freshly fetched version as a substitute for the caller's version. For application-managed
versions, also explicitly configure the version increment and `AllowWrite` for that token.

For a shadow/named scalar token, use
`ExpectVersion<int, PatchRequest>("Version", request => request.ExpectedVersion)` instead of a CLR
member lambda. The model name and type are checked at generation time; no token assignment,
retry or fixture shadow state is implied. See [typed mapped-property bindings](../repositories/MAPPED-PROPERTY-BINDINGS.md).

An omitted patch with no active setters returns `NoChanges`. On a pre-save failure, the command
restores its own tracker changes while preserving pre-existing pending edits. It does not attach
an input graph and mark every property modified. Optional
[bounded leaf-child reconciliation](COLLECTION-RECONCILIATION.md) is explicitly designer-configured;
arbitrary graph mapping and business validation remain application adapters or EF configuration.

Use optional [ReturnsSaved](SAVED-COMMAND-RESULTS.md) to project fresh saved scalars/related DTOs after
a Create/Patch save or [bounded PatchMany save](SAVED-BATCH-RESULTS.md). Unlike `Returns`, it re-queries by the complete primary key without tracking;
it does not reapply the mutation predicate or promise atomic returning. Result-read failures report
`RepositorySavedResultException`, since the write is already saved.

## Outcomes are deliberate

| Outcome | Meaning |
| --- | --- |
| `Applied` | Saved tracked command or a successful conditional set-based write |
| `Staged` | Tracker changed; no save happened |
| `NotFound` | Tracked selection found no visible row |
| `Conflict` | The tracked command's expected version was stale or its save lost a race |
| `NoChanges` | No configured patch setter was active |
| `NotMatched` | Conditional set-based/upsert update matched zero rows; absence vs version/owner mismatch is not guessed |
| `Inserted`, `Updated`, `AlreadyExists` | Optional atomic-provider outcomes |

`HasValue` says whether the configured saved result exists. `AffectedRows` for a tracked command
is the **shared save count**, not a promise that only this entity was persisted. A `Save()` command,
like repository `SaveChangesAsync`, saves all pending changes in that context.

Use `Update(...).ExpectVersion(...).WithOutcome()` for a compare-and-set SQL update. Named `Delete()`
is set-based and immediate; `TrackedDelete().Stage()` stages a single fetched row, while
`TrackedDelete().Save().Returns(...)` can return its pre-delete data after a successful save.
Stage many/save once is separate from immediate set-based bulk deletion; fetches and SQL batches
mean neither path promises one universal network round trip.

With an explicitly selected native writer, `Update(...).ReturnsMany(...)` and
`Delete().ReturnsMany(...)` return a `MutationResult<T>` for all directly affected rows. They do not
fetch first, call `SaveChanges`, synchronize tracking, or accept `Stage`/`Save`. Standalone result
failures roll back; existing explicit transactions use savepoints. This is a different capability
from the tracked single-value `Returns` terminal. See [Returning writes](RETURNING-WRITES.md).

The generator does not retry a stale client version into success. Conflicts in a different pending
entity during a shared save throw `RepositoryWriteConflictException`, rather than attributing them
to the currently called command. Database/constraint failures otherwise remain provider exceptions.

`RepositorySavedResultException` means the save succeeded but reload/result mapping failed. Do not
repeat the write blindly. An enclosing uncommitted transaction can still be rolled back. Commit
observer failures are separately reported as committed failures; they must not trigger a write retry.

The [Ledger designer](../../../samples/ConfigurationExamples/Ledger/Configuration/LedgerRepositoryDesigner.cs) and
[runnable example](../../../samples/ConfigurationExamples/Ledger/Program.cs) demonstrate generated values,
null patches, conflicts, shared scope and staged deletion. Tests also execute competing versioned
writes, owner predicates, constraint failures, tracker restoration and exactly-once lease disposal.
See EF's [optimistic concurrency documentation](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
for the underlying original-value/save behavior.
