# Named reads on write repositories

CQRS separates contracts, not every database read from every command. A command may need to
load current state or an aggregate before applying an application-owned rule. Configure that
read in the designer rather than assemble selectors in the service:

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults().UseCqrsSelectors());
modelBuilder.Entity<WorkItem>(entity =>
{
    entity.HasLoadProfile("Edit", load => load.Include(item => item.Labels));
    entity.HasQuery("LoadForUpdate")
        .OnWriteRepository()
        .Where<int>((item, id) => item.Id == id)
        .UseLoadProfile("Edit")
        .AsTracking()
        .SingleOrDefault();
});
```

The write consumer calls `await writes.LoadForUpdateAsync(id, cancellationToken)`. The read
port does not expose this operation. Loading does not save anything. In selector mode an
application can edit the returned tracked graph and call the shared `SaveChangesAsync`, or use
a separately configured typed command. Business rules and graph reconciliation remain in the
application/adapter; RepoGen does not infer them.

## Defaults and placement

`OnWriteRepository()` is optional on either the entity query builder or the current result
builder, including after `Select`, `From`, or grouping. Omitted configuration means the query
stays on the read repository, exactly as before. `OnWriteRepository(false)` restores that default.
The public design inventory exposes `RepositoryQueryDesign.Target` (`Read` or `Write`).

The option requires `UseCqrsSelectors()` or `UseClosedRepositories(...)`; combined compatibility
selectors have no separate destination. No additional CLI flag, resolver, cache, lease, writer
package, or unit-of-work service is required. Existing module/exclusion settings still apply.

`ReadEnabled = false` omits read-target queries, not command-side queries. `WriteEnabled = false`
omits commands and write-target queries. Disabled operations are not expanded, resolved, bound,
or recorded as active operations; they do not need valid unused projection/profile references.
A keyless entity has no write port. An active write-target query on it is diagnosed rather than
silently relocated to the read surface.

Each operation has one selected destination. To expose similar behavior on both ports, declare
two named queries deliberately. Identical CLR method/input signatures are still rejected across
queries/commands because the combined implementation cannot implement duplicate operations with
different tracking behavior. This is contract segregation, not an authorization boundary.

## Tracking and concurrency

Read-target queries retain the independent `UseReadOnlyTracking(...)` policy, no-tracking by
default. Write-target queries instead inherit `UseDefaultTracking(...)`; when unspecified they
use the selected context's ordinary tracking policy. An explicit query `.AsTracking()` or
`.AsReadOnly()` takes precedence. These decisions stay in the designer even if generated runtime
tracking methods are hidden or renamed.

`LoadForUpdate` is a user-chosen name, **not** a locking hint or a concurrency guarantee. There
is no implicit transaction, row lock, `SELECT FOR UPDATE`, save, authorization, or stale-write
retry. Choose an explicit unit of work/transaction and expected-version command where needed.
Loading and checking a DTO, then issuing another command, can race with another client. Expected
versions or conditional SQL predicates must enforce the actual write precondition.

Pure scalar/DTO projections do not track entities merely because their policy says tracking.
Tracking does not overwrite or detach already tracked state. A tracked query may reuse that
state; it is not a force-refresh operation. In selector mode keep entity graphs inside their
borrowed context scope. `SaveChangesAsync` persists all changes on that shared context, not only
those belonging to the repository used for the save.

## Closed, EF-free command contracts

Use a DTO/result projection when selecting the closed surface:

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("My.Application.Repositories"));
modelBuilder.Entity<DeskTask>(entity =>
{
    entity.BindWritePort<ITaskDeskWrites>();
    entity.HasProjection(task => new TaskSummary(task.Id, task.Title));
    entity.HasQuery("LoadForRename")
        .OnWriteRepository()
        .Where<int>((task, id) => task.Id == id)
        .ProjectTo<TaskSummary>()
        .SingleOrDefault();
});
```

The application-owned write port can contain both
`Task<TaskSummary?> LoadForRenameAsync(int id, CancellationToken cancellationToken = default)`
and matching configured command methods. Exact input overloads, nullable annotations, inherited
members, saved-result shapes and default interface methods retain the existing binding rules.
Read-port binding cannot pick up a write-target query, or vice versa.

Closed command reads have the same bounded EF-free public type validation as read-model queries:
no mapped entities, query/context handles or nested ORM escapes in requests/results. A closed
load returns application data, not an EF aggregate that can later be attached through the port.
Use a typed command or an explicit handwritten aggregate adapter for the mutation. Named load
profiles, typed adapters, grouped/result pipelines, membership, pagination, registered providers
and streaming keep their existing semantics and provider limits.

## Context, visibility and caching

The operation uses the same direct context or async/keyed lease strategy as the other operations.
With a shared unit of work it participates in the active transaction and fixed context key;
without one, operation-owned contexts are still disposed after the read (or stream enumeration).
A separate owned read followed by an owned write does not create a shared transaction or tracker.
Input guards execute before acquisition. Existing visibility/global-filter policies, reviewed
navigation access and explicitly configured bypasses still apply.

Enabled caching on a command-side query is rejected (`RGD2103`), including an explicitly
no-tracking command read. Cache hits must not stand in for current command-state loading or
pretend to populate a change tracker. Generating a cache decorator does not prevent command
reads: they execute against the selected context without caching, while ordinary read-target
cached queries and save invalidation retain their configured behavior. Globally disabled caching
still suppresses unused cache configuration as before. No distributed freshness guarantee is
implied.

## Inventory and compatibility

The operation manifest adds `repositoryTarget: "write"` only to write-target query entries.
Default query inventory and output bytes are unchanged. Closed API/data snapshots place the
operation on the write contract and include only its enabled request/result data types. Moving
an existing read operation to the write port is a breaking change for read consumers; compatibility
checks report the removal, and the new write-member requirement for handwritten implementers.
Regeneration never silently accepts an approved API baseline.

Diagnostics:

- `RGD2101`: placement selected without a CQRS/closed surface.
- `RGD2102`: active command read selected on a keyless entity with no write capability.
- `RGD2103`: enabled caching selected for a command read.
- `RGD2104`: a command-read signature collides with a generated write/save helper; rename it or disable that optional helper.

See the runnable [CQRS example](../../../samples/ConfigurationExamples/Cqrs/Program.cs) and
[EF-free adapter example](../../../samples/ConfigurationExamples/Adapters/Program.cs), with configuration
in the [Workboard](../../../samples/ConfigurationExamples/Cqrs/Configuration/WorkboardRepositoryDesigner.cs) and
[TaskDesk](../../../samples/ConfigurationExamples/Adapters/Configuration/TaskDeskRepositoryDesigner.cs) designers.
