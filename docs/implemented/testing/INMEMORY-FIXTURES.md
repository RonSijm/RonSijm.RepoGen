# Optional shared in-memory fixture stores

RepoGen can generate shared, typed **test-data fixtures** alongside scripted closed repositories.
The designer chooses row shapes, key types and copy adapters; generated test doubles receive the
same fixture through their constructor. No production dependency or registration changes unless
testing generation and test registration are explicitly selected.

This delivers F15.2's storage foundation: seeds, duplicate policies, detached inspection, shared
context partitions and baseline capture/restore. Storage alone does not execute designer operations;
they still need scripts unless separately opting into [portable fixture reads](INMEMORY-QUERIES.md)
or [designed stateful writes](INMEMORY-WRITES.md), including [bounded batches](INMEMORY-BATCHES.md), with explicit Stage/Save and checkpoint rollback.
The manifest states the selected
execution boundary explicitly.

## Configure storage in the RepositoryDesigner

Assume the input `NotesContext` maps a public `Note` and its application port uses DTO results:

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("MyApp.Contracts.Generated")
    .UseScriptedTestDoubles("MyApp.Tests.Generated")
    .UseInMemoryTestStores());

modelBuilder.Entity<Note>(entity =>
{
    entity.HasInMemoryStore(note => new NoteKey(note.Id), note => new Note
    {
        Id = note.Id,
        Name = note.Name,
        Labels = note.Labels.ToList()
    });
    // Production operations are still declared normally, e.g. HasQuery("Find")...
});
```

`NoteKey` can be a public readonly record struct; string, Guid, numeric and explicitly composed
immutable keys also work. Keys are supplied by seeds: no database-generated IDs, time values or
business defaults are inferred. Consumer seed factories can deterministically supply them.

`UseInMemoryTestStores()` is off by default, including both presets. It requires closed/scripted
testing output and at least one selected `HasInMemoryStore` declaration. Entities without a store
still get ordinary scripts and receive the same shared fixture. `GenerateTestDouble(false)` excludes
both the entity's double and its store; it does not remove production operations.

`UseInMemoryTestStores(false)` disables stores while leaving ordinary scripted generation intact.
Declarations are ignored when disabled, so production designers can retain test configuration without
emitting it. `WithoutInMemoryStore()` removes one declaration without excluding that test double.
Presets preserve an explicitly selected store setting; they do not enable it implicitly.

## Public storage proxies instead of database model dependencies

The entity-shaped overload stores the entity itself. That entity must be public and the testing
project must reference its assembly. Application contracts remain unchanged, but that assembly
may itself bring a model/EF dependency into the testing project.

Use an explicit public data shape to avoid that dependency, including for internal database entities:

```csharp
entity.HasInMemoryStore<NoteSeed, NoteKey>(
    seed => seed.Key,
    seed => new NoteSeed { Key = seed.Key, Name = seed.Name, Labels = seed.Labels.ToList() });
```

Only `NoteSeed` appears as the store's row type. It can live in an independent test-data project.
This adapts **fixture storage**, not the entity's production expressions: no arbitrary entity-to-proxy
mapping is inferred. Add [MapInMemoryEntity<TData>](INMEMORY-PROXIES.md) for explicit scalar read
adaptation with per-operation coverage. Without it, a script can deliberately inspect this proxy
and return the application's DTO. Writes are never reverse-mapped by that read declaration.

The [MemoryPartitions designer](../../../samples/ConfigurationExamples/MemoryPartitions/Configuration/PartitionDirectoryRepositoryDesigner.cs)
demonstrates this: an EF model uses `PartitionSetting`, while the fixture stores EF-free `SettingSeed`.
Its runnable consumer references neither Models nor EF.
The [MemoryProxies designer](../../../samples/ConfigurationExamples/MemoryProxies/Configuration/ProxyDirectoryRepositoryDesigner.cs)
additionally adapts public seed data to an internal EF model's selected scalars, enabling automatic
reads without constructing or referencing that model in the consumer.

## Generated output and usage

For `NotesContext`, generation adds these types to the existing separate TestDoubles output:

| Generated type/member | Purpose |
| --- | --- |
| `NotesContextRepositoryFixture` | Shared fixture root |
| `NotesContextRepositoryData : InMemoryDataContext` | One partition containing every selected store |
| `Data.NoteStore : InMemoryStore<Note, NoteKey>` | Typed seed/inspection surface; names use entity name plus `Store` |
| `INoteRepositoryTestDouble.Fixture` | Access to the same fixture received by every selected double |
| `NoteRepositoryTestDouble(NotesContextRepositoryFixture)` | Explicit shared constructor dependency |

SingleFile includes data, fixture, scripts and registration in one testing file. Split layouts put
shared data/fixture declarations in the context registration file, not repeated per entity. These
types/files participate in the existing ownership, freshness and collision checks. There is no new
output category or CLI configuration DSL: use `RepositoryOutputLocations.TestDoubles` as before.

```csharp
var fixture = new NotesContextRepositoryFixture();
var repository = new NoteRepositoryTestDouble(fixture);
fixture.Data.NoteStore.Seed(new Note { Id = 1, Name = "One" });
fixture.Data.CaptureBaseline();

repository.OnFindAsync.Runs((args, _) =>
{
    var note = repository.Fixture.Data.NoteStore.Find(new NoteKey(args.Id));
    return Task.FromResult(note is null ? null : new NoteSummary(note.Id, note.Name));
});
```

That handler is an **explicit test implementation**, not a generated query. Before arranging it,
the call throws `UnarrangedRepositoryOperationException`, even if data was seeded. No visibility,
validation, ordering or projection behavior is silently promised.

## Arrangement and detached inspection

| Store method | Behavior |
| --- | --- |
| `Seed(row, duplicateKeys)` | Add one detached row; duplicate default is Throw |
| `SeedRange(rows, duplicateKeys)` | Enumerate once and add the complete range atomically |
| `ReplaceWith(rows, duplicateKeys)` | Replace current contents atomically; retain baseline |
| `Find(key)` | Detached row or null |
| `Snapshot()` | Read-only outer list of detached rows |
| `Count` | Current arranged row count |
| `Remove(key)` / `Clear()` | Immediate fixture arrangement changes, not repository writes |

`InMemoryDuplicateKeyBehavior.Throw` rejects keys already present or repeated in input. `Ignore`
retains the first/current row; `Replace` uses the last supplied row. Invalid policy values fail
before enumeration. Enumeration, duplicate or adapter failures leave the entire arrangement
unchanged. Default equality is `EqualityComparer<TKey>.Default`. The designer's optional
[HasInMemoryKeyComparer<TKey>](INMEMORY-KEY-COMPARERS.md) emits an explicit store comparer for key
identity only; it does not rewrite predicates, relationship matching or provider collation. Direct
runtime `CreateStore` and `KeyedInMemoryFixture` constructors also accept explicit comparers;
context-partition comparison is a separate choice, not changed by an entity-store setting.

All seed and inspected roots pass through the configured copy adapter. Null results, the identical
root instance, null keys or a changed key under the configured comparer are rejected. Mutable nested
lists/graphs must be copied by the adapter: a new root alone is not a deep copy. Adapters must also
not mutate their input. There is no reflection/JSON cloning, graph reconstruction or live-reference
mode. Keys themselves must be immutable with stable equality, including any members of composite keys.
Inspection/baseline copying also checks that a row's key still matches its arranged dictionary key;
a bad shallow copier that lets a key-bearing member change fails explicitly instead of returning
a mismatched row. This guard does not make an incorrect copier a deep copy.

Snapshot list order is **not** a repository sorting guarantee. This infrastructure applies no EF
filters, navigation fixup, tracking, relational constraints or provider collation. An explicit script
decides its observed behavior; real-provider tests remain necessary.

## Baselines and isolation

`data.CaptureBaseline()` prepares detached copies of every store, then commits all baselines together.
`data.RestoreBaseline()` prepares new detached copies of every baseline, then replaces all current
stores together. If any copy/key adapter fails, no store/baseline changes. An uncaptured baseline is
empty. Stores registered after capture also start with an empty baseline.

`data.Clear()` empties current rows in all stores without discarding baselines. Restoring does not
replace data/store objects, so references held by repository scripts stay valid. Inspection or later
arrangement cannot mutate a correctly copied baseline; repeated restores produce independent rows.

`ResetTestDouble()` still resets scripts/counts/history only, not data. `ClearCalls()` likewise leaves
data and scripts intact. Use baseline restore separately to select exactly which state to reset.
These are fixture resets, **not** tracked Save/rollback or database transactions.

Context operations are synchronized, including range enumeration and copying. Adapters/enumerators
must be pure and cannot re-enter their own data context; re-entry fails before a nested operation
can change it. Avoid blocking or calling other fixtures from an adapter. User code with external
side effects is not rolled back. Different partitions have independent context locks.

## Typed context partitions and explicit lifetime

When production design uses `UseKeyedContextLeases<TContextKey>()`, its fixture uses the same
public key type instead of a fixed `Data` property:

```csharp
var east = fixture.For(new PartitionKey("east", "primary"));
var west = fixture.For(new PartitionKey("west", "primary"));
east.NoteStore.Seed(/* explicit row */);
east.CaptureBaseline();
east.RestoreBaseline(); // Does not reset west.
var createdKeys = fixture.ContextKeys;
```

Equal immutable keys return the same data/store instances; other keys are isolated. ContextKeys
is a read-only inventory snapshot, not a list of database connections. Reset affects one selected
partition, not every key. Scripts must explicitly choose their partition from request arguments;
the store layer alone does not execute `UseContextKey`. The optional read backend does reuse that
designer binding automatically; neither mode authorizes tenancy.

`AddNotesContextRepositoryTestDoubles()` registers a scoped fixture with TryAdd and keeps all
selected repository aliases scoped. Each scope normally starts with isolated data and scripts.
To intentionally share **data** across request scopes in one test fixture:

```csharp
services.AddSingleton(new NotesContextRepositoryFixture());
services.AddNotesContextRepositoryTestDoubles();
```

Existing explicit fixture registration is preserved. Script instances remain scoped even when data
is singleton. This never changes production registrations automatically; call test replacement after
production registration. Sharing an entire repository double/its scripts is a separate deliberate DI choice.

## Receipts, dependencies and examples

`data.CaptureSnapshot(storeName, ...)` copies selected stores together under one partition's
context lock and returns `InMemoryDataSnapshot`. Its typed `GetRows<TRow>(storeName)` checks the
captured name and exact row type. Names are case-sensitive, duplicate names copy once, an empty
selection captures no stores and all names are validated before any copier runs. Captured
collections are read-only, rows are detached, and later arrangement/reset does not replace them.
The declared adapters still own deep-copy quality. Copy failures/reentrant adapters publish no
snapshot and do not alter rows/baselines. Only key/copy adapters run under the capture lock;
query predicates, projections and other application behavior run after it is released.

The [multi-source read backend](INMEMORY-QUERIES.md#multi-source-queries-use-the-same-designer-definitions)
uses this API automatically, selecting all participating roots once per invocation/enumeration.
It is test arrangement coordination, not relational isolation or a Stage/Save transaction.
[Explicit navigation reads](INMEMORY-NAVIGATION.md) use the same snapshot boundary, then load
only declared, accessed mapped-key relationships outside the lock. Loading never changes stores/baselines.

With automatic reads disabled, the manifest retains `mode: Scripted` and operation `RequiresOverride` statuses. Its optional
`testDoubles.stores` receipt reports fixture/data types, context key, row/key shapes, exact copy/key
sources, lifetime, baseline scope, explicit seed keys and `FixtureArrangementOnly` writes.
`executesDesignerPipeline: false` prevents confusing storage alone with an automatic repository backend.
Enabling [portable reads](INMEMORY-QUERIES.md) adds per-operation coverage and explicit execution semantics.

The optional Testing runtime remains .NET 8 and EF/model/designer/generator-independent. Optional
compiler validation needs the chosen public row/key input assemblies in `testDoubleReferences`,
in addition to framework/contracts/Testing/DI abstractions. A model assembly is necessary only if
that is where the chosen row/key types live; a separate data proxy can keep the testing boundary EF-free.
`RGD3201` covers prerequisites, unselected/missing stores and invalid type/expression adapters;
`RGD3202` reserves the fixture property against application-port method collisions.
`RGD3204` covers missing/mismatched store-key declarations and non-portable comparer factories.

- [MemoryFixtures](../../../samples/ConfigurationExamples/MemoryFixtures/README.md): two repositories,
  scoped shared stores, typed keys, nested copy isolation and all-store baseline reset.
- [MemoryPartitions](../../../samples/ConfigurationExamples/MemoryPartitions/README.md): typed context
  keys, a public storage proxy, per-key reset and explicit singleton data across scoped scripts.
- [MemoryKeyIdentity](../../../samples/ConfigurationExamples/MemoryKeyIdentity/README.md): opt-in key
  comparer, unchanged predicates, public mapped writes, duplicate policies and baseline/checkpoint restore.
- [MemoryImmediateWrites](../../../samples/ConfigurationExamples/MemoryImmediateWrites/README.md): separately
  opted-in immediate scalar Update/Delete/consume, original-row setters, pending isolation and rollback.
- [MemoryAtomicWrites](../../../samples/ConfigurationExamples/MemoryAtomicWrites/README.md): separately opted-in
  scalar insert/upsert, composite conflict targets, update-only visibility/versions and explicit fixture IDs.
- [MemoryCollections](../../../samples/ConfigurationExamples/MemoryCollections/README.md): separately opted-in
  bounded child-store reconciliation, explicit public maps, owner-derived FKs, child visibility/versions,
  reserved IDs, shared staging/save and checkpoint rollback.
- [MemoryJoins](../../../samples/ConfigurationExamples/MemoryJoins/README.md): automatic designed joins,
  visibility and reports from coordinated snapshots of two entity-shaped stores.
- [MemoryNavigation](../../../samples/ConfigurationExamples/MemoryNavigation/README.md): declared reference/collection
  relationships, nested DTOs and unfiltered profiles from one coordinated graph snapshot.

These include named designers, small input contexts, checked-in output and exact declaration-to-output
maps. Their consumers have no EF/model/designer/generator dependency. Continue with
[scripted operations](SCRIPTED-TEST-DOUBLES.md) for matchers, failures, recording and streaming.
