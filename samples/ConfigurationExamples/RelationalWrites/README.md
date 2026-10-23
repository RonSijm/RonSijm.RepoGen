# RelationalWrites: test real writes, not scripted persistence

Start with [FixtureTicketsRepositoryDesigner](Configuration/FixtureTicketsRepositoryDesigner.cs).
It configures two repositories on one [FixtureTicketsContext](Model/FixtureTicketsContext.cs):
tickets have generated IDs and an explicit concurrency token; audit rows have a real foreign key.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) emits three artifacts:

| Declaration | Exact generated result |
| --- | --- |
| Closed repositories, ticket reads/writes and audit writes only | [IReadFixtureTicketRepository / IWriteFixtureTicketRepository / IWriteFixtureAuditRepository](../Contracts/Generated/FixtureTicketsRepositories.g.cs) |
| HasQuery("Find") | FindAsync returns a TicketSummary through the [production implementation](Generated/Repositories.g.cs) |
| Create.Save.Returns | CreateAsync persists first, then maps the database-generated ticket ID |
| StageRename.Patch.ExpectVersion.Stage | StageRenameAsync protects the original version and stages only approved Title/Version assignments |
| StageAudit.Create.Stage | StageAuditAsync stages the second entity without saving |
| UseAsyncContextLeases | Operations take the configured lease provider; Program deliberately binds both to one real scoped unit of work |
| Selected policies | [Manifest](Generated/manifest.json); no testing-double output or fake write/backend option is enabled |

Request/result inputs are under [Contracts](Contracts/CreateTicket.cs); the model/designer compile
separately from the generated consumer, preserving the code-first bootstrap boundary.

## What Program.cs proves

[Program.cs](Program.cs) uses the optional relational harness with an explicitly owned `:memory:`
SQLite database. It creates a ticket and verifies a saved generated ID, stages changes through both
write ports, saves them together, then rolls back the real transaction. A fresh scope verifies that
neither change remains. It also rejects a stale version, stages the valid patch/audit, saves once,
and verifies the committed rows from another fresh context.

Application operations remain named and typed. Context/SQL inspection lives only in fixture code;
the orchestration uses the neutral IRepositoryUnitOfWork/IRepositoryTransaction contracts.
The shared save count is across both repositories, not one repository and not a SQL round-trip promise.
There is no cache, tenant key, native writer, automatic reset or mock-persistence simulation.

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Check -Run -ValidateCompilation
dotnet run --project samples/ConfigurationExamples/RelationalWrites -c Release
```

See [contract suites and provider fixtures](../../../docs/implemented/testing/REPOSITORY-CONTRACT-FIXTURES.md).
[RelationalContractFixture](../RelationalContractFixture/README.md) shares portable read cases with a fake;
[MemoryCoverage](../MemoryCoverage/README.md) demonstrates why a scripted write result is not persistence.
