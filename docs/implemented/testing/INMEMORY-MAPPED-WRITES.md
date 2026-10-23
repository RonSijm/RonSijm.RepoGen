# Explicit mapped scalar fixture write state

`MapInMemoryWrites(..., useMappedPropertyState: true)` separately permits exact
[MapInMemoryProperty](INMEMORY-MAPPED-READS.md) bindings as complete scalar fixture state.
It supports shadow, field-backed, field-only, indexer and ordinary mapped values without invoking
entity constructors/getters/indexers. It is off by default; a read binding alone still grants no writes.

Start with [RevisionNotebookRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryMappedWrites/Configuration/RevisionNotebookRepositoryDesigner.cs)
and its [exact designer → output map](../../../samples/ConfigurationExamples/MemoryMappedWrites/README.md).
The one-table example uses a shadow concurrency token and an explicit factory matching its computed
column. The consumer calls only named application-port operations, not EF or a runtime property DSL.

## Configure complete state deliberately

```csharp
entity.HasInMemoryStore<NoteSeed, int>(row => row.Id,
        row => new(row.Id, row.Title, row.Counter, row.Revision))
    .MapInMemoryEntity<NoteSeed>(row => new StoredNote
        { Id = row.Id, Title = row.Title, Counter = row.Counter })
    .MapInMemoryProperty<NoteSeed, int>("Revision", row => row.Revision)
    .MapInMemoryWrites<NoteSeed>((row, saved) => new(row.Id, row.Title, row.Counter,
        EF.Property<int>(row, "Revision")), useMappedPropertyState: true)
    .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Counter + 1);

entity.HasCommand("Rename")
    .Where<NoteChange>((row, request) => row.Id == request.Id)
    .Patch(setters => setters
        .SetProperty<string, NoteChange>(row => row.Title, (row, request) => request.Title)
        .SetProperty(row => row.Counter, row => row.Counter + 1))
    .ExpectVersion<int, NoteChange>("Revision", request => request.ExpectedRevision)
    .Save()
    .ReturnsSaved(row => new NoteCard(row.Id, row.Title, EF.Property<int>(row, "Revision")));
```

The final model must declare Revision as a generated integer concurrency token. The example's
SQLite expression is Counter + 1; neither that expression nor token advancement is inferred by
the fixture generator. `EF.Property`, mapped members and exact typed/cast indexers in the designer
lower to safe typed slots. No EF dependency, runtime reflection lookup or property-name API is
emitted into the fixture consumer.

Every EF scalar needs exactly one complete initializer or named binding. The public-data encoder
must preserve every value a supported command can produce. Before shared commit, runtime checks
verify detached copies, the primary key and the full structural read → write → read round trip.
Custom tracked equality does not make a lossy encoder acceptable. Binary values retain detached
copy/structural comparison semantics; public row copy recipes must also copy mutable values.

## Independent choices

| Choice | Behavior |
| --- | --- |
| Read binding only | Detached reads; no implicit write permission |
| Separate write-state opt-in | Complete scalar state, expected versions and approved scalar command expressions |
| Named generated-value factory | Exact final-model name/type; explicit create/update preparation only |
| ExpectVersion | Caller-supplied original condition; not an assignment or token-advancement rule |
| Stage / shared Save | Saved rows remain unchanged until Save; original tokens are rechecked before commit |
| ReturnsSaved | Fresh scalar/DTO read after Save, with the existing complete-key/result policies |

`HasInMemoryGeneratedValue<TValue>(string propertyName, factory)` and
`WithoutInMemoryGeneratedValue(string propertyName)` supplement the ordinary member overloads.
Names refer to model properties, not database columns. Exact model CLR types, generated metadata,
pure portable factories, sentinels, declaration ordering and independent provider guards still apply.
Factories are emitted, never executed during generation. An expected-version declaration never
advances the token; configure application-managed advancement deliberately in every relevant writer.
Application-managed named tokens without a supported setter or generated-value factory still need
a typed override for advancement; the read/write-state binding itself is not a setter.
Separate [named mapped command setters](../writes/MAPPED-COMMAND-SETTERS.md) now explicitly assign
eligible non-identity scalars on Patch/ordinary Update, including caller-advanced shadow tokens.
Their targets, write-state consent, protected-token permission and original checks remain independent.

Tracked scalar create/patch/delete, bounded root-row batches and separately opted-in immediate
scalar/atomic commands reuse existing outcomes and pending-state semantics. Only original tracked
tokens participate in shared-Save conflict checks; immediate commands execute against saved rows
without flushing staged work. Script precedence, cancellation, partitions, checkpoints and baselines
remain unchanged. Automatic reads can be disabled while retaining scalar writes and saved DTOs.
Scalar filters and helper translations still need their own existing permissions.
Atomic production mappings can separately select [native scalar capture](../writes/ATOMIC-SCALAR-CAPTURE.md)
for returned shadow/field/indexer values. Complete fixture state and per-command immediate consent
remain independent. Keys/conflict/assignment targets are still ordinary CLR scalars; no new private
or shadow setters, graph state or provider guarantees are inferred.

Native Update/Delete returned scalars can separately combine this complete write map with
IncludeShadowProperties/IncludeMappedProperties and SimulateImmediateWrites. The
[returned-value guide](INMEMORY-MAPPED-RETURNING.md) describes post-update/pre-delete values and
pre-commit mapping without simulating a provider buffer or changing production capture guards.

## Identity and graph boundaries

Mapped write-state bindings cannot supply primary/alternate keys, unique-index members or foreign
keys. Those still need ordinary explicit scalar assignments, complete same-type primary-key aliases
and the existing ownership policies. Read relationship-key permission remains a separate feature.

Existing designed bounded collection/link reconciliation can separately combine this complete
write-state consent on each mapped participant with SimulateCollectionWrites. The
[mapped collection guide](INMEMORY-MAPPED-COLLECTIONS.md) covers original parent/child tokens,
factories, lossless shared Save and independent filter/read permissions without relationship inference.

This opt-in alone does not enable collections, new private/shadow setter targets, native returned
buffers, owned/inherited/shared graphs, cascades, EF change
tracker entries, database rowversion, SQL constraints or concurrency-race/provider equivalence.
Production [named scalar setters](../writes/MAPPED-COMMAND-SETTERS.md) and
[bounded named PatchMany item setters](../writes/MAPPED-BATCH-SETTERS.md) are separate explicit
designer capabilities. They can reuse this complete fixture state without broadening identity,
generated-column, child/graph or native/atomic target permissions.
Separately configured [mapped reconciliation setters](../writes/MAPPED-RECONCILIATION-SETTERS.md)
can reuse each participant's complete state under existing bounded collection/link execution;
parent, child, command and saved-read permissions remain independent.
Unsupported commands retain `RGD3402` and explicit typed overrides; invalid/incomplete maps retain
`RGD3601`, invalid factories `RGD3501`, and ambiguous scalar sources `RGD4901`.

Redeclaring MapInMemoryWrites replaces the flag; omitting it restores the default restriction.
WithoutInMemoryWrites clears consent and factories, retaining read bindings and arrangement data.
Removing the scalar map or replacing/removing the store clears dependent state. Disabling automatic
commands or entity writes emits no write-state/value registrations. Production contracts, SQL,
API/cache shapes and unconfigured generated files are unchanged.

## Inspect receipts

Store `writeMapping.mappedPropertyState` and supported operation `mappedPropertyState` receipts
list exact model names/types, safe slots, recipes and access kinds. They record the separate opt-in,
full structural round-trip requirement, ordinary identity boundary, explicit-only token advancement
and lack of provider guarantees. Read receipts distinguish separately permitted write-row use;
they do not claim EF tracker simulation or automatic mapping inversion.

[Public write adapters](INMEMORY-PROXY-WRITES.md) · [Generated values](INMEMORY-GENERATED-VALUES.md) ·
[Mapped reads](INMEMORY-MAPPED-READS.md) · [Testing index](README.md)
