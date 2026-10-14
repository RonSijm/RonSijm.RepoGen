# Designer-owned SQL command timeouts

Command timeouts are optional policies for named closed operations. Without configuration,
RepoGen keeps the selected context/provider setting. Services do not receive a timeout argument,
an EF configuration API, or a new infrastructure dependency.

Configure a model default, an entity override, or an individual query/command override:

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("MyApp.Contracts.Generated")
    .UseCommandTimeout(30));

modelBuilder.Entity<Order>(entity =>
{
    entity.UseCommandTimeout(15);

    entity.HasQuery("FindNames")
        .UseCommandTimeout(5)
        .OrderBy(order => order.Id)
        .Select(order => order.Name)
        .List();

    entity.HasQuery("Report")
        .Results<Order>()
        .GroupBy(order => order.Region, group => new
        {
            Region = group.Key,
            Count = group.Count(),
            Total = group.Sum(order => order.Amount)
        })
        .OrderBy(total => total.Region)
        .Select(total => new RegionReport(total.Region, total.Count, total.Total))
        .UseCommandTimeout(60)
        .Paged();

    entity.HasCommand("DeleteExpired")
        .UseCommandTimeout(10)
        .Where(order => order.Expired)
        .Delete();

    // Retain the context/provider setting, even though entity/model defaults exist.
    entity.HasQuery("CountUsingContextSetting")
        .UseCommandTimeout(null)
        .Count();
});
```

These choices work with directly injected contexts and with optional owned, borrowed, or keyed
leases. Neither context resolution nor a unit of work is required just to configure a timeout.

## Inheritance and reset

The precedence is **operation → entity → model → selected context/provider**.

| Configuration | Meaning |
| --- | --- |
| Not configured | Inherit the next level |
| `UseCommandTimeout(positiveSeconds)` | Use that SQL timeout while the named operation runs |
| Entity/query/command/result `UseCommandTimeout(null)` | Bypass inherited designer timeouts; retain the context's existing setting |
| Entity/query/command/result `InheritCommandTimeout()` | Remove only that override; inherit the next level again |
| Model options `UseCommandTimeout(null)` | Clear the model default; use the context/provider unless a lower-level override exists |

`RepositoryResultQueryBuilder<TResult>` configures its **whole named query**, not just the next
stage. This includes grouped reports, `From(...)` joins and registered provider query roots.
Continue from the latest result builder: a superseded builder cannot change the active policy.
An explicit null is not a request for an infinite timeout, nor does it clear an existing timeout
on the context. Zero and negative seconds are rejected; retain the provider setting if it has
special semantics that RepoGen does not model.

Entity overrides apply to configured named reads (including command-side reads) and commands.
They do not change legacy CRUD/selector methods or add timeout controls to application ports.
Overrides require `UseClosedRepositories(...)`: the generated method must own the complete
operation/stream lifetime. Applying one to an included entity in compatibility/CQRS-selector mode
fails with `RGD2302`, instead of silently doing nothing. Excluded entities and inactive operations
do not activate their policies. Invalid override values fail with `RGD2301`.

## Scope, saves, and failures

For an effective numeric timeout, the generated method captures the selected context's existing
setting, applies the timeout, and restores that exact setting on exit, including an original null.
Restoration happens before an owned lease disposes its context. Input guards and pre-canceled
calls run before acquisition; they do not change the context.

- A counted page applies its timeout to both count and item SQL. Grouped, dictionary, scalar,
  joined, registered-root and native-write operations use the same policy mechanism.
- A stream retains the policy during enumeration and restores it on completion, failure,
  cancellation or early enumerator disposal. Merely obtaining an enumerable does not acquire a
  context or change its setting. Always dispose the enumerator, including when abandoning it.
- A saved tracked command includes its fetch, save and optional reload in the scope. The save
  still persists all pending changes on the shared context, not only this repository's entity.
- A staged command restores its policy before returning. A later standalone unit-of-work save
  uses the context's own setting, not the already-exited staging method's timeout. A staged
  create can execute no SQL at all; a staged delete/patch may fetch a row.
- Set-based writes and optional SQLite/SQL Server native atomic/returning writers honor the
  scoped setting. Their existing transaction, rollback and commit-notification semantics remain
  unchanged. A timeout is not an automatic retry policy.
- Cache hits, failed fills and post-commit invalidation failures restore the setting. Restoration
  does not undo an already committed write; committed-failure exceptions still describe that
  outcome accurately.

This is a **per-SQL-command timeout**, not a total-operation deadline or cancellation timer. For
example, a page's two commands can each use the full configured timeout. Enforcement and exact
timeout exceptions belong to the database provider. Cancellation remains a separate token.

The scope temporarily changes mutable context state. EF contexts already require sequential
use: do not overlap operations or keep a stream open while executing another operation on the
same shared context. Operations using separate owned contexts do not share this state.

## Output and example

Unconfigured/reset overrides leave generated output unchanged. Configured overrides add
`commandTimeout: { "seconds": ..., "source": "entity" | "operation" }` to the relevant manifest
entity/operation. An explicit context-setting override records `seconds: null`; an inherited model
default remains in the existing top-level `commandTimeoutSeconds` field. Timeout changes do not
alter application method signatures or API compatibility snapshots.

The runnable [Reports example](../../../samples/ConfigurationExamples/Reports/Program.cs) verifies
actual SQL command timeouts: both commands of each grouped page use its three-second operation
override; the left join explicitly retains the context's seventeen-second setting. Its
[designer](../../../samples/ConfigurationExamples/Reports/Configuration/ReportingRepositoryDesigner.cs) also shows
model/entity defaults. Other examples need no new settings or infrastructure.
