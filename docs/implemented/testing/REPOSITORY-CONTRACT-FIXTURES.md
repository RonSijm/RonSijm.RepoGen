# Optional contract suites and real-provider fixtures

F15.6 adds two independent testing tools. Neither changes generation defaults, production registration
or the service-facing repository API. There is no new designer flag: select the testing package and
wire its provider/fixture explicitly in test infrastructure. RepositoryDesigner remains the source
of truth for the operations being exercised.

## Run the same application-port cases on different backends

`RonSijm.RepoGen.Testing` stays EF-free. A `RepositoryContractSuite<TRepository>` is an immutable,
named set of asynchronous assertions over the application's own port. It enumerates case definitions
once, rejects duplicate names and runs sequentially, stopping at the first failure. Each case gets
a fresh explicitly supplied `RepositoryContractSession<TRepository>`.

```csharp
var suite = new RepositoryContractSuite<INotes>(new[]
{
    new RepositoryContractCase<INotes>("missing lookup", async (notes, token) =>
    {
        if (await notes.FindAsync(99, token) is not null)
            throw new InvalidOperationException("A missing row must return null.");
    })
});

await suite.RunAsync(token =>
{
    var fixture = CreateFreshSeededGeneratedFixture();
    return ValueTask.FromResult(new RepositoryContractSession<INotes>(fixture.Repository));
});
```

The session does not infer ownership of the repository. Supply its scope/fixture cleanup callback
when needed; disposal is idempotent, and concurrent disposals await the same completion. Sessions
close even when an assertion fails or is canceled. Assertion/setup/cleanup failures report the case
name and phase through `RepositoryContractFailureException`; simultaneous assertion and cleanup
failures are both preserved. Cancellation of the supplied token propagates as cancellation unless
cleanup also fails, in which case both errors are retained.

The session factory owns seed/reset isolation. A fresh DI scope alone does not reset a database or
singleton fixture. Use immutable criteria and explicitly isolated data, just as in ordinary tests.
Assertions can use a test framework or ordinary exceptions; the runner has no test-framework dependency.

## Exercise the real generated EF implementation

The separate optional `RonSijm.RepoGen.Testing.EntityFrameworkCore` package provides
`RelationalRepositoryFixture.CreateAsync<TContext>`. It deliberately references EF; neither the
ordinary Testing package nor application contracts acquire that dependency. The fixture does not
choose SQLite/SQL Server, register generated repositories automatically, or construct a designer.

```csharp
await using var fixture = await RelationalRepositoryFixture.CreateAsync<NotesContext>(options =>
{
    options.ConfigureServices = services =>
    {
        // Your test-owned provider/connection plus the actual generated production registration.
        services.AddDbContext<NotesContext>(builder => builder.UseSqlite(testConnection));
        services.AddNotesContextRepositories();
    };
    options.InitializeDatabaseAsync = (context, token) => context.Database.EnsureCreatedAsync(token);
    options.SeedAsync = SeedNotesAsync;
    options.ResetDatabaseAsync = ResetOwnedTestDataAsync;
});

await using var session = await fixture.OpenSessionAsync();
var notes = session.GetRepository<INotes>();
var result = await notes.FindAsync(1);
```

Register the context **scoped**, so repositories and fixture infrastructure in one session resolve
the same instance. Missing/singleton/transient context registrations fail early. Scopes/provider use
DI validation; sessions reject EF InMemory rather than label it relational verification. A session
is not a transaction and does not implicitly save. Configure the actual generated lease/resolver,
unit-of-work, cache and provider registrations when those features are part of the test.

`Context` and `Services` are exposed for test setup/inspection and resolving infrastructure such as
`IRepositoryUnitOfWork`. They need not appear in services or application repository contracts.
Generated repositories may acquire their own configured leases; registering a scoped context does
not force a custom resolver/provider to use it. All selected contexts must target the explicitly
owned test database. Do not use one session/DbContext concurrently.

## Explicit setup, reset and resource ownership

| Hook/action | Behavior |
| --- | --- |
| ConfigureServices | Required, explicit provider and application registrations |
| InitializeDatabaseAsync | Optional; runs once at creation in an owned scope |
| SeedAsync | Optional; runs after initialization and after each explicit reset |
| ResetDatabaseAsync | Optional; defines the complete reset policy, including any deliberate schema work |
| OpenSessionAsync | Fresh scope/context, no implicit seed, transaction or save |
| ResetAsync | Requires the reset callback and no active sessions; resets, then reseeds in a fresh scope |
| Fixture disposal | Closes all tracked sessions and the owned provider, attempting all cleanups even if one fails |

Defaults perform **no schema creation, deletion, migration or database reset**. With no reset
callback, ResetAsync fails clearly. The fixture cannot determine whether a supplied connection
string is safe to destroy. Callbacks may delete only explicitly test-owned data/databases; use a
dedicated temporary/test database, never production or an application's working database.

Reset is not an implicit database transaction or retry. A failing/canceled callback can leave
partially reset state; the scope and lifecycle guard are still released. Close every session before
resetting. Opening another session, concurrent reset and fixture disposal are refused while reset
runs; await reset before disposal. Callbacks must not re-enter the fixture or dispose their own scope.

DI owns resources created by factory/type registrations. An externally supplied instance/connection
remains caller-owned; dispose it after the fixture. The examples register an opened `:memory:` SQLite
connection through a singleton factory, so fixture disposal closes it and its isolated database.
The harness does not imply that arbitrary connection/provider registrations are isolated.

Initialization/reset errors are not replaced by cleanup errors. If both fail, an aggregate retains
both; fixture cleanup likewise attempts every tracked scope and root provider. Disposed sessions
and fixtures reject further access/open/reset, and repeated disposal shares its original completion.

## Pairing a real fixture with shared contract cases

```csharp
await suite.RunAsync(async token =>
{
    await fixture.ResetAsync(token); // Explicit case isolation; no sessions may remain open.
    var session = await fixture.OpenSessionAsync(token);
    return new RepositoryContractSession<INotes>(session.GetRepository<INotes>(), session.DisposeAsync);
});
```

If your custom factory fails after opening a session but before returning the contract wrapper,
it must dispose that session itself. The runner can only clean up a session it actually receives.

Shared portable assertions test observable operation contracts, not universal SQL/fake equivalence.
Keep provider-only tests separate: SQL translation/collation/null compensation, navigations, cache
invalidation, generated values, concurrency races, relational transactions and native writers require
the actual selected provider. SQLite success does not establish SQL Server correctness.

## Focused RepositoryDesigner examples

- [RelationalContractFixture](../../../samples/ConfigurationExamples/RelationalContractFixture/README.md):
  one designer emits production and automatic-fixture reads; one named application-port suite
  checks both implementations, with fresh fake data and explicit SQLite reset between cases.
- [RelationalWrites](../../../samples/ConfigurationExamples/RelationalWrites/README.md): two generated
  repositories share the real unit of work; generated IDs, expected versions, Stage/shared Save
  and transaction rollback are verified on SQLite, not simulated by a fake.

The [portable read matrix](INMEMORY-QUERIES.md) remains bounded. [Declared entity-shaped navigation
reads](INMEMORY-NAVIGATION.md) are available, with shared generated SQLite cases and a focused
[MemoryNavigation example](../../../samples/ConfigurationExamples/MemoryNavigation/README.md).
[Stateful tracked fixture writes](INMEMORY-WRITES.md) now provide explicit scalar Create/Patch/Delete,
versions, Stage/shared Save and checkpoint rollback, with generated SQLite comparisons and a focused
[MemorySharedWrites example](../../../samples/ConfigurationExamples/MemorySharedWrites/README.md).
[Bounded fixture batches](INMEMORY-BATCHES.md) also have shared generated SQLite cases and a focused
[MemoryBatches example](../../../samples/ConfigurationExamples/MemoryBatches/README.md).
[Explicit scalar generated-value factories](INMEMORY-GENERATED-VALUES.md) have additional shared
actual-generated SQLite cases and the focused
[MemoryGeneratedValues example](../../../samples/ConfigurationExamples/MemoryGeneratedValues/README.md).
[Mapped public navigation reads](INMEMORY-PROXY-NAVIGATION.md) also have shared actual-generated
SQLite cases and a focused [MemoryProxyNavigation designer](../../../samples/ConfigurationExamples/MemoryProxyNavigation/README.md).
[Public scalar fixture write adapters](INMEMORY-PROXY-WRITES.md) also have shared actual-generated SQLite
cases and a focused [MemoryProxyWrites designer](../../../samples/ConfigurationExamples/MemoryProxyWrites/README.md).
Additional generated-value/provider policies and graph/native write simulation remain
separate roadmap work. The real-provider harness is an alternative verification tool, not their implementation.
[Explicit public scalar read mappings](INMEMORY-PROXIES.md) are available; arbitrary entity behavior
is not adapted by them.
