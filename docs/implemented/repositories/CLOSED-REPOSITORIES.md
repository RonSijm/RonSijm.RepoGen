# Closed repositories, async contexts, and units of work

Closed-operation repositories are opt-in. Existing generated selectors, combined repositories,
direct context injection and their save semantics remain the compatibility default.

## Configure a closed API

```csharp
protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
{
    modelBuilder.Configure(options => options
        .UseClosedRepositories("My.Application.Repositories")
        .GenerateOnlyConfiguredEntities()
        .UseAsyncContextLeases()); // Omit for ordinary borrowed DbContext injection.

    modelBuilder.Entity<Product>(entity =>
    {
        entity.HasProjection(product => new ProductSummary(product.Id, product.Name));

        entity.HasQuery("Search")
            .Where<string>((product, term) => product.Name.Contains(term))
            .OrderBy(product => product.Id)
            .ProjectTo<ProductSummary>()
            .Paged();

        entity.HasCommand("DeleteById")
            .Where<Guid>((product, id) => product.Id == id)
            .Delete();
    });
}
```

The public surface contains `IReadProductRepository`, `IWriteProductRepository`, and a combined `IProductRepository`. `SearchAsync(term, page, pageSize, cancellationToken)` returns `RonSijm.RepoGen.Abstractions.PagedResult<ProductSummary>`. `DeleteByIdAsync(id, cancellationToken)` returns the affected-row count. These are real instance methods, so application tests can substitute the interfaces directly.

The read port has no selector, mutation, save, or tracking API. Entities and EF query infrastructure do not appear in its signatures. The implementation uses internal selectors and projection helpers in the persistence output. Configured read-target queries default to no-tracking, independently of `UseDefaultTracking(...)` and the context's tracking default. `UseReadOnlyTracking(...)` and explicit designer query policies can override this; consumers do not choose tracking at runtime. See [Tracking policies and API names](TRACKING-POLICIES.md).

`HasQuery(...).OnWriteRepository()` optionally puts a named read on the write capability instead,
including application-owned write-port binding. It still returns EF-free DTOs/results, inherits
the ordinary write tracking policy, and cannot enable caching. Read consumers cannot call it;
write-only entities may still declare it. It does not save or lock rows. See
[Command-side queries](../queries/COMMAND-SIDE-QUERIES.md).

The static type boundary also checks public DTO/request bases, interfaces, data members, indexer
inputs, constructors, helper signatures and generic constraints. `RGD1502` reports the exact
offending path, including relational/provider and ADO query handles. Expanding generic DTO graphs
are bounded without skipping closed arguments. An optional independent contract/implementation
compiler check is available to code-first runners; see [Compiler validation](../generation/COMPILATION-VALIDATION.md).

Named `.Update(...)` / `.Delete()` commands are **immediate set-based operations**. They are not staged writes and do not need `SaveChangesAsync`. Zero affected rows means no row matched; it is not a generated distinction between missing, stale, and unauthorized input. Typed `Create` / `Patch` / `TrackedDelete` instead require explicit `Save()` or `Stage()` and support expected-version outcomes and saved DTOs. See [typed commands](../writes/TYPED-COMMANDS.md) and optional [native returning writes](../writes/RETURNING-WRITES.md).

## Generate two outputs

```powershell
dotnet run --project src/RonSijm.RepoGen.Tool -- generate `
  --project path/to/My.Database.csproj `
  --designer-project path/to/My.RepositoryDesigner.csproj `
  --generate-selectors `
  --output path/to/My.Persistence/Generated/Repositories.g.cs `
  --contracts-output path/to/My.Application/Generated/RepositoryContracts.g.cs `
  --manifest-output path/to/repositories.json
```

Closed mode includes projection/query helpers in `--output`; do **not** also specify `--projection-output`. The latter remains the compatibility-mode option. If existing attribute-generated selectors are detected, remove those attributes in the persistence project, or use the appropriate generation setup so they do not conflict with the internal selector types; `--generate-selectors` requests the standalone selectors, but does not suppress the separate attribute generator.

The Application/Contracts project needs only `RonSijm.RepoGen.Abstractions` and its own public request/response types. The persistence project references that project, EF Core, and `RonSijm.RepoGen.EntityFrameworkCore`. The designer references the entity and application-type projects. Application must **not** reference persistence.

On first generation, build the request/DTO and schema/designer projects before introducing services that reference not-yet-generated ports. The independent [code-first runner and opt-in build checks](../generation/CODE-FIRST-GENERATION.md) provide a repeatable bootstrap without depending on generated persistence. Normal builds do not secretly regenerate files.

## Bind application-owned ports

If the Application project already defines narrow interfaces:

```csharp
public interface IProductSearch
{
    Task<PagedResult<ProductSummary>> SearchAsync(
        string term, int page, int pageSize,
        CancellationToken cancellationToken = default);
}

// Add alongside the matching HasQuery configuration:
entity.BindReadPort<IProductSearch>();
entity.BindWritePort<IProductDeletion>();
```

Import `RonSijm.RepoGen.Abstractions` for the result types. Every abstract method on each bound interface, including inherited members, must match a configured operation on that capability. Names, parameter order/types, return shape, and nullability are validated. The generator does not reinterpret incompatible ports or implement business logic. Application-owned default interface methods remain on the application interface. Properties/events, generic methods, ref/out parameters, and static interface operations are rejected.

Generated facades inherit the explicitly bound ports, and registration exposes those ports through the same implementation. Bind a base port explicitly if it also needs its own DI registration. Methods use the `Async` suffix convention. Overloads bind by exact input CLR signature; binding one does not hide unbound overloads. Return types and reference annotations cannot distinguish C# overloads. Name each configured operation to match its port; automatic entity/model/ID adaptation remains a handwritten boundary.

Expression trees erase reference annotations. Prefer a public request DTO for nullable fields, or explicitly use `.AllowNullInput<string>("term")` on a query/command/result builder. It validates a declared reference input's exact type and marks its outer parameter nullable. The predicate must still deliberately handle null. Nullable value types already survive expression trees; nested generic reference annotations are not inferred by this override.

Choose a generated-contract namespace distinct from persistence repository names and from existing ports with the same facade names. Name collisions are diagnosed rather than generating a self-inheriting or duplicate interface.

## Select entities and operations

```csharp
entity.RepositoryEnabled = false; // Excludes the entity entirely.
entity.WriteEnabled = false;      // Closed read-only port.
entity.ReadEnabled = false;       // Closed write-only port.
entity.ExcludeQuery("InternalReport");
entity.ExcludeCommand("DangerousMaintenance");
```

Read/write capability flags work in closed mode or optional [CQRS-selector mode](CQRS-SELECTORS.md). Application-port bindings and EF-free contract output require closed mode. Capability flags are diagnosed in compatibility mode, whose selectors still have combined capabilities. Keyless entities have no generated closed write port; configuring their write commands is rejected. This is compile-time capability segregation, not authorization.

Reusable configuration can implement `IRepositoryEntityConfiguration<TEntity>` or `IRepositoryModelConfiguration`, then be applied with `modelBuilder.ApplyConfiguration(new ProductConfiguration())`. Direct registrations run in registration order. Opt-in `ApplyConfigurationsFromAssembly(assembly, predicate)` discovers modules in ordinal full-type-name order; select a namespace or marker to keep different designers separate. Later scalar settings override earlier settings; operation/projection registrations accumulate unless explicitly excluded. Duplicate closed operation names are diagnosed. See [configuration modules](../generation/CONFIGURATION-MODULES.md) for discovery rules and an entity configuration example.

## Choose a context lifetime

Without `UseAsyncContextLeases`, generated operations take the scoped context directly and never dispose it. Register the context normally, then call generated `services.AddMyDbContextRepositories()` (the name uses the actual context class).

With the option enabled, operations take `IDbContextLeaseProvider<MyDbContext>`:

- `ScopedDbContextLeaseProvider<TContext>` borrows an existing context.
- `FactoryDbContextLeaseProvider<TContext>` uses EF's `IDbContextFactory<TContext>.CreateDbContextAsync` and owns/disposes each context.
- A custom provider may asynchronously select/create a context, returning `DbContextLease.Owned(context)` or `DbContextLease.Borrowed(context)` explicitly.
- `IKeyedDbContextLeaseProvider<TContext,TKey>` plus `FixedKeyDbContextLeaseProvider<TContext,TKey>` lets application infrastructure bind a typed database key once per request or unit of work. Alternatively configure `UseKeyedContextLeases<TKey>()` and `UseContextKey<TRequest,TKey>(request => request.Database)` on every named operation. `KeyedRepositoryUnitOfWork` binds its first request and rejects key changes; acquire that first lease before starting its transaction. RepoGen does not interpret keys or infer tenancy.

For operation-owned contexts:

```csharp
services.AddDbContextFactory<MyDbContext>(options => options.UseSqlite(connectionString));
services.AddScoped<IDbContextLeaseProvider<MyDbContext>, FactoryDbContextLeaseProvider<MyDbContext>>();
services.AddMyDbContextRepositories();
```

An owned lease is disposed after materialization, failure, or cancellation. Streaming acquires lazily and retains the lease until enumeration completes or the enumerator is disposed; consumers must dispose enumerators when stopping early. A borrowed lease never disposes its context. Providers are responsible for cleaning up partially created resources if acquisition itself fails.

## Share a unit of work

`RepositoryUnitOfWork<TContext>` acquires one lease lazily and retains it until disposal. Register the **same scoped instance** as both `IRepositoryUnitOfWork` and the repositories' lease provider:

```csharp
services.AddScoped<RepositoryUnitOfWork<MyDbContext>>(provider =>
    new RepositoryUnitOfWork<MyDbContext>(
        new FactoryDbContextLeaseProvider<MyDbContext>(
            provider.GetRequiredService<IDbContextFactory<MyDbContext>>()),
        provider.GetServices<IRepositoryCommitObserver>()));
services.AddScoped<IRepositoryUnitOfWork>(provider =>
    provider.GetRequiredService<RepositoryUnitOfWork<MyDbContext>>());
services.AddScoped<IDbContextLeaseProvider<MyDbContext>>(provider =>
    provider.GetRequiredService<RepositoryUnitOfWork<MyDbContext>>());
services.AddMyDbContextRepositories();
```

Tracked writes staged through the shared context use the unit of work's `SaveChangesAsync`. Closed set-based commands execute immediately and record their invalidation tags with the shared unit of work. They can participate in its explicit transaction:

```csharp
await using var transaction = await unitOfWork.BeginTransactionAsync(
    IsolationLevel.Serializable, cancellationToken);

await firstRepository.DeleteByIdAsync(firstId, cancellationToken);
await secondRepository.DeleteByIdAsync(secondId, cancellationToken);
await unitOfWork.SaveChangesAsync(cancellationToken); // Only needed for additional tracked changes.
await transaction.CommitAsync(cancellationToken);    // Does not implicitly SaveChanges.
```

`IsolationLevel` here is `System.Data.IsolationLevel`; the default is `Unspecified` (provider-native isolation), and explicit level support depends on the EF provider. No timeout or automatic retry policy is imposed by default. Contexts and repositories are not safe for concurrent operations. Disposal rolls back unfinished transactions. After rollback or a failed commit, discard the unit of work rather than reuse an inconsistent change tracker. Ambient/external transactions are rejected because their final commit is not owned by this unit of work.

`UseCommandTimeout(seconds)` is an optional closed-operation SQL policy. It can be configured on
model options, entities and individual query/command/result builders, with operation → entity →
model precedence. An explicit entity/operation null retains the context setting;
`InheritCommandTimeout()` removes that override. Numeric policies restore the previous context
timeout afterward, including streams. They are per-command limits, not operation deadlines or
retry policies. A staging method does not carry its timeout into a later unit-of-work save.
See [Command timeouts](COMMAND-TIMEOUTS.md) for scope and failure semantics.

`RepositoryTransactionRunner.ExecuteAsync` is an explicit infrastructure helper for replayable transactions using the provider execution strategy. It requires fresh owned contexts on every attempt, keeps scope stable, creates a new work/transaction, runs the callback, saves, then commits. Borrowed/reused contexts are rejected. Callbacks must not perform non-idempotent external side effects; unknown commit outcomes still require application idempotency/verification. Stale client versions and committed observer failures are not retried into write success. See EF's [transaction guidance](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

## Commit observers and caching boundaries

Optional infrastructure-only `IRepositorySaveParticipant<TContext>` adapters can be registered on
ordinary/keyed work before its first save. They stage tracked rows on the actual selected context
inside an explicit work-owned transaction, before EF saving and changed-tag collection. Staging,
set-based writes and commit alone do not invoke them. Preparation/save failures require rollback
and fresh work/context state; the hook supplies no message/dispatch or durability policy. See
[Save participants](SAVE-PARTICIPANTS.md) for registration, re-entry/cancellation and retry semantics.

`IRepositoryCommitObserver` receives de-duplicated changed tags after a database commit. In an explicit transaction, intermediate saves and set-based commands accumulate tags; observers run only after `CommitAsync`, and never for a rollback. Tracked writes default to the changed EF type's full name; pass a `tagSelector` to `RepositoryUnitOfWork` for application-defined/dependent tags. Named set-based commands record the generator's dependency tags. Observers should not re-enter the same unit of work.

All observers are attempted. `RepositoryCommitObserverException` means **the database committed but an observer failed**. `RepositoryCommitCleanupException` similarly identifies cleanup failure after commit. Both derive from `RepositoryCommitException`; do not retry the database write blindly. Observers are an extension seam, not a durable outbox or guaranteed distributed cache consistency mechanism.

The legacy cache decorator remains available in compatibility mode. Optional closed caching uses a separate EF-free adapter, actual acquired scope, dependency tags and commit-coordinated invalidation. See [Scoped caching](SCOPED-CACHING.md). `GenerateCacheDecorator(false)` disables configured caching; `UseClosedCaching` explicitly enables it and requires a safe scope/lifetime configuration. Custom work implementations must support the commit-registration contract or fail before a cached write.

## Validation and manifest

Add `--validate` to load the model and render/validate all selected outputs without writing generated files. Unless `--no-build` is supplied, project compilation still runs and may update normal `bin`/`obj` output. This is model/render validation, not a substitute for compiling the generated consumer projects or testing provider translation.

`--manifest-output` writes a deterministic JSON inventory: context/lifetime/surface, entity capabilities, bound ports, operation names, parameter/result types, tracking, and caching. It does not contain connection strings or query results. There is no timestamp, so identical designs produce identical manifests. Duplicate output paths and configuration errors are detected before writing generated output. Filesystem failures during writing are not an atomic multi-file transaction.

Named result pipelines also record their terminal, ordered stage kinds/input types, conditional-stage flags, default-sort/consistency policy and command persistence/version metadata. See [Result pipelines](../queries/RESULT-QUERY-PIPELINES.md), [Typed commands](../writes/TYPED-COMMANDS.md), and [Optional providers](PROVIDER-CAPABILITIES.md).
