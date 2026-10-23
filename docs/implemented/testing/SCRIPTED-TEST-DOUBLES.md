# Optional scripted repository test doubles

RepoGen can emit test-only substitutes for **closed repository contracts** from the same
RepositoryDesigner that defines production operations. They implement generated read/write/combined
interfaces and existing bound ports, without referencing EF, the context or production repositories.
Ordinary scripted generation does not need entities. Optional shared stores can separately select
an entity/public storage shape. Ordinary generation is unchanged unless explicitly enabled.

This delivers the scripted portions of F15.1/F15.5 and focused F15.6 examples. Optional
[in-memory fixtures](INMEMORY-FIXTURES.md) now add F15.2's shared stores, seeds, detached copies and
baseline reset. Script-only mode still requires explicit arrangements. Separately opting into
[portable fixture queries](INMEMORY-QUERIES.md) now executes supported designer reads, with scripts
taking precedence and explicit coverage failures for other shapes. Writes/Stage/Save/rollback
simulation remains planned.

## Configure in the designer

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("MyApp.Contracts.Generated")
    .UseScriptedTestDoubles("MyApp.Tests.Repositories"));

modelBuilder.Entity<Note>(entity =>
{
    entity.HasQuery("Find").Where<int>((note, id) => note.Id == id)
        .ProjectTo(note => new NoteSummary(note.Id, note.Name)).SingleOrDefault();
});
```

`UseScriptedTestDoubles(null)` disables testing output (disable dependent store/read/strict options first if also selected).
Neither preset enables it, and presets
do not erase an explicit testing namespace. Per-entity `GenerateTestDouble(false)` excludes that
fixture without changing production generation; `GenerateTestDouble()` enables it, and
`GenerateTestDouble(null)` restores model inheritance. Entity opt-in requires model testing/closed
namespaces. Ordinary entity/query/command exclusions and read/write capability switches still apply.

The context and designer are generation inputs only. Fixtures need application DTOs/requests/ports,
not the entity assembly. Compatibility/CQRS **selector** APIs are intentionally not supported here:
they expose EF/entity behavior. Closed contracts already split read/write capabilities.

## Separate output and optional runtime

```csharp
var bundle = RepositoryGenerator.Generate(context, new NotesRepositoryDesigner());
bundle.Write(new RepositoryOutputLocations(
    "Persistence/Generated/Repositories.g.cs",
    Contracts: "Application/Generated/Repositories.g.cs",
    Manifest: "Persistence/Generated/manifest.json",
    TestDoubles: "Tests/Generated/RepositoryTestDoubles.g.cs"));
```

The test project references application contracts and the optional `.NET 8`
`RonSijm.RepoGen.Testing` package/project. This runtime has no EF, model, generator or compiler
dependency; it brings DI abstractions for generated registration helpers. Reference a DI container
separately if building a service provider. Production/application projects need no Testing reference.

SingleFile emits one testing file. PerEntity/PerContract emit one fixture per selected entity plus
one context registration file, and take testing **directories**. Testing output participates in
distinct-path checks, ownership, freshness, multi-context type collisions and obsolete-file receipts.
Generation never deletes old files. Use distinct testing namespaces for contexts sharing entity names.
Writing test output requires an explicit destination; there is no fallback into production/contracts.

The CLI's `--test-doubles-output <path>` chooses the location; configuration still belongs in the
designer. The flag does not enable testing. `--validate` needs no output location and writes nothing;
`--check` includes test-output freshness.

## Generated fixture and exact ports

For `Note`, generation emits:

- `NoteRepositoryTestDouble`: a sealed context-free implementation of the combined closed port.
- `INoteRepositoryTestDouble`: a test-only fixture interface inheriting that port, with typed
  scripts, `ResetTestDouble()` and `ClearCalls()`.
- `OnFindAsyncArguments` and `OnFindAsync`: typed inputs and `ScriptedOperation<Arguments, NoteSummary?>`.
- `AddNotesContextRepositoryTestDoubles()`: explicit scoped registration.

Scripts preserve configured return/input types and merged bound-port parameter names/nullability.
Optional arguments work through the declared application interface; concrete implementations require
ordinary inputs explicitly and default the final token, matching the production convention.
Handwritten default interface methods retain their bodies and can call arranged underlying operations.
They are not converted into independent scripts.

Overloads have separate scripts, for example `OnFindAsyncByInt32` / `OnFindAsyncByString`.
Ambiguous friendly type labels use deterministic signature hashes. The generated file/manifest are
authoritative for names. Fixture control collisions fail with a diagnostic. Application IDs, nullable
DTOs, page/dictionary/command wrappers and stream items retain their configured types.

## Arrange responses, failures and handlers

```csharp
var fixture = new NoteRepositoryTestDouble();
IReadNoteRepository read = fixture;

fixture.OnFindAsync.Returns(null);
fixture.OnFindAsync.When(args => args.Id == 1,
    (_, _) => Task.FromResult<NoteSummary?>(new(1, "One")));
fixture.OnFindAsync.EnqueueException(new InvalidOperationException("temporary failure"),
    args => args.Id == 2);
fixture.OnFindAsync.EnqueueResult(new NoteSummary(2, "Two"), args => args.Id == 2);
```

`When` registers persistent matching rules. `Enqueue`/`EnqueueResult`/`EnqueueException` register
one-shot rules. Rules run in registration order, **before** the fallback installed by `Returns`,
`Throws` or `Runs`. First match wins. A queued rule is consumed once, including when its handler
fails; unmatched entries remain available. An earlier broad persistent rule can shadow later queues.

Without a matching rule/fallback, calls throw `UnarrangedRepositoryOperationException` identifying
context/entity/full overload. No empty collection, default count or successful write is fabricated.
`Throws(exception)` sets a persistent failing fallback. `Runs((args, token) => Task<T>)` supplies a
real async handler, including genuinely incomplete tasks for deterministic completion/failure tests.
Replacing the fallback does not remove matching rules. No delays, clocks, seeds or business rules
are inferred.

Writes are scripts too: arrange the appropriate `CommandResult`, conflict or affected-row value.
An arranged Applied/Staged result changes no store. Native/temporal/registered SQL operations can
be scripted at their port boundary, but SQL, validation, visibility and transaction semantics are
not executed or simulated. Keep provider-backed tests for those guarantees.

## Recording, snapshots and reset

Invocation counts are always available. Argument history is **off by default**. Enabling it
requires an explicit snapshot adapter:

```csharp
fixture.OnSearchAsync.RecordCalls(
    args => new(new SearchRequest(args.Request.Ids.ToArray())),
    maximumCalls: 50);

await repository.SearchAsync(request);
var call = fixture.OnSearchAsync.Calls[0];
var suppliedIds = call.Arguments.Request.Ids;
var outcome = call.Outcome;
```

The adapter runs at invocation and again on inspection. Copy nested mutable requests/lists/graphs;
returning the same mutable reference is **not** isolation. Immutable inputs can use an identity copy.
There is no automatic JSON/reflection clone or reverse mapping. Snapshot failures propagate before
consuming arrangements and do not fabricate history entries. Default capacity is 100; positive
capacities retain the newest calls ordered by invocation sequence.

Calls expose sequence, copied arguments, outcome and error type/message. Outcomes are InProgress,
Completed, Failed, Canceled and (for early stream disposal) Disposed. Result/exception objects are
not retained. Supplied arguments and error messages may still be sensitive: use suitable bounds/copies.

- `ClearCalls()` clears counts/history, keeping arrangements and recording policy.
- `ResetTestDouble()` clears all scripts, counts/history, retaining recording policy.
- Individual scripts expose `Reset()`, `ClearCalls()` and `StopRecording()`.
- `StopRecording()` stops future capture, retaining existing calls.

Reset/clear do not cancel in-flight handlers; their completion does not repopulate cleared history.
Rule/count/history updates are synchronized. Consumer matchers/copy adapters/handlers run outside
locks. Arrange before concurrent usage. This does not make mutable results/delegates thread-safe or
provide an atomic reset across all scripts in a fixture.

## Cancellation and streams

Cancellation is checked before selecting/consuming a rule and after awaiting its handler.
Pre-canceled calls are counted/recorded as canceled but leave queued entries available. Handlers
must cooperate to interrupt unfinished work promptly; the runtime does not abandon background tasks.

Stream operations use `ScriptedStreamOperation<Arguments, TItem>`. `ReturnsItems(items)` captures
the outer sequence while arranging, but does not deep-copy items. Use `Returns`/`Runs` for custom
`IAsyncEnumerable<TItem>` behavior.

Streams are cold: the first MoveNextAsync begins a call; re-enumeration is a new invocation.
Both method and enumeration/WithCancellation tokens are honored. Full exhaustion plus disposal is
Completed; early disposal is Disposed. Factory, iteration, current-item and disposal failures
propagate and record failure. The wrapper disposes the underlying enumerator on exhaustion,
failure or early disposal. Consumers must dispose their enumerator; await foreach handles this.
If disposal itself replaces an earlier iteration/cancellation exception, as normal async iterator
cleanup can, the recorded failure describes the exception escaping to the caller.

## Explicit test registration

```csharp
services.AddNotesContextRepositoryTestDoubles(); // Test composition root only.
using var scope = provider.CreateScope();
var fixture = scope.ServiceProvider.GetRequiredService<INoteRepositoryTestDouble>();
var read = scope.ServiceProvider.GetRequiredService<IReadNoteRepository>();
```

The fixture, concrete type, generated CQRS/combined ports and directly bound application ports use
one scoped instance per selected entity. Another scope gets a fresh fixture. The helper **explicitly
replaces** these aliases; repeated calls do not duplicate concrete registrations. It leaves unrelated
services alone and needs no context/provider. Call it after production registration when replacing
ports in an existing service collection. Nothing activates it automatically.

With optional [shared stores](INMEMORY-FIXTURES.md), selected doubles also receive the same scoped
context fixture. An explicitly registered fixture lifetime is preserved; scripts remain scoped by
default. Data capture/restore and script reset are deliberately separate operations.

## Manifest and optional compilation

The manifest's `testDoubles` section lists mode/namespace, fixtures, and each selected operation's
signature, return type, script name and stream flag. Every script is `RequiresOverride`, explaining
that a typed arrangement is required; `executesDesignerPipeline: false` states the execution boundary.
Default interface helper bodies are not reported as fake query implementations.

Optional compiler validation includes test source in implementation compilation, requiring Testing/
DI abstractions in that reference list. For an additional independent EF-free boundary check, pass
`testDoubleReferences` containing target-framework, Abstractions, Testing, DI abstractions and
independently built manual DTO/port inputs. Generated contracts + test files compile separately with
stage TestDoubles and `TestDoublesChecked`. Like the contract stage, it does not include arbitrary
additional implementation/consumer sources or discover dependencies automatically.
See [compiler validation](../generation/COMPILATION-VALIDATION.md).

`RGD3101` covers missing closed/testing prerequisites; `RGD3102` covers unsupported scripted shapes/
fixture control collisions. Namespace `RGD1801` and ordinary `RGG` output checks also apply.

## Examples and remaining roadmap

- [ScriptedPorts](../../../samples/ConfigurationExamples/ScriptedPorts/README.md): overloaded bound read/write
  ports, queued outcomes, snapshotted recording, scoped aliases and a default interface helper.
- [ScriptedStreams](../../../samples/ConfigurationExamples/ScriptedStreams/README.md): one read-only stream,
  cold invocation, early disposal and enumeration cancellation.

Both contain a named RepositoryDesigner, tiny input model/context, checked-in generated contract/
testing/production source and a declaration-to-output map. Their consumers have no EF dependency.

Shared keyed stores, seed/baseline/snapshot APIs and explicit row/key/copy adapters are now available
through [in-memory fixtures](INMEMORY-FIXTURES.md), with [MemoryFixtures](../../../samples/ConfigurationExamples/MemoryFixtures/README.md)
and [MemoryPartitions](../../../samples/ConfigurationExamples/MemoryPartitions/README.md) examples.
Optional [portable reads](INMEMORY-QUERIES.md), [declared navigation reads](INMEMORY-NAVIGATION.md)
and [tracked scalar writes/shared Stage/Save/rollback](INMEMORY-WRITES.md) now reuse supported designer
operations, including suitable [bounded patch/delete batches](INMEMORY-BATCHES.md), with scripts retaining override precedence. [Contract suites and relational fixtures](REPOSITORY-CONTRACT-FIXTURES.md)
share assertions and exercise actual production implementations. [Explicit scalar generated-value
factories](INMEMORY-GENERATED-VALUES.md) support suitable ID/default/computed/token policies.
[Mapped public navigation reads](INMEMORY-PROXY-NAVIGATION.md) are also supported.
[Explicit public scalar write adapters](INMEMORY-PROXY-WRITES.md) separately support internal models.
Additional generated-value/provider policies and graph/native write simulation remain extensions. A script tests
a service's reaction to arranged outcomes; it does not establish persistence correctness.
