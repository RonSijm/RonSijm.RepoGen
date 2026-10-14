# MemoryProxyWrites: public fixture rows, internal models, explicit writes

Read [ProxyNotebookRepositoryDesigner](Configuration/ProxyNotebookRepositoryDesigner.cs) first.
It adds a **separate scalar write adapter** to public data mappings: immutable public seed rows
back internal EF entities, without referencing or constructing those entities in this executable.
The [Program](Program.cs) is the consumer/assertion walkthrough, not generation configuration.

## This designer produces these files

| Designer input | Exact generated output | What to inspect |
| --- | --- | --- |
| Closed CQRS operations, PrivateNote/PrivateAudit | [Contracts/Generated/ProxyNotebookRepositories.g.cs](../Contracts/Generated/ProxyNotebookRepositories.g.cs) | Read/write contracts with request/DTO shapes only |
| Named List/Create/Rename/Remove/batch and staged operations | [Persistence/Repositories.g.cs](Persistence/Repositories.g.cs) | Actual generated EF operations; excluded from this executable |
| Stores, scalar read/write maps, generated-value factories | [Generated/TestDoubles.g.cs](Generated/TestDoubles.g.cs) | Public stores, private scalar rows, MapWriteStore registration, command/fixture DI |
| Selected operation coverage and write adapter | [Generated/manifest.json](Generated/manifest.json) | ExplicitScalarWriteAdapter, full scalar/key validation and Generated operation receipts |

[GenerationRunner](../GenerationRunner/Program.cs) explicitly pairs `ProxyNotebookContext` with this
designer and these destinations. The shared [Models](../Models/Models.csproj),
[Contracts](../Contracts/Contracts.csproj) and [Designers](../Designers/Designers.csproj) projects compile
the adjacent `Model`, `Contracts` and `Configuration` inputs; they are not alternative designers.

## Design-time choices → consumer behavior

- `HasInMemoryStore<NoteSeed, int>` declares a public immutable row with different property names
  and arrangement-only `Tag`. Its copier owns binary values.
- `MapInMemoryEntity` declares **all** persistence scalar slots. The entity initializer is inspected,
  not invoked: constructors, default property initializers and setters are not fixture behavior.
- `MapInMemoryWrites((candidate, saved) => ...)` explicitly reconstructs public data. The candidate
  is a generated scalar row. `saved` preserves the current `Tag`; creates receive null. Runtime
  round-trip validation rejects lossy values/changed keys before any shared commit.
- ID and version factories explicitly simulate saved/pending-aware keys and computed title length.
  This is not a promise of SQL identity, rowversion, collation or cross-process race parity.
- Production declarations also supply visibility, expected versions, approved conditional setters,
  result DTOs and bounded batch matching. There is no second fake/service mutation DSL.
- Stage keeps public saved stores unchanged; Save is shared across PrivateNote and PrivateAudit.
  Batch results keep input order. Binary inputs, saved rows and DTOs are detached.
- Checkpoint rollback and baseline restore recover public rows and row-derived allocation inputs.

The executable verifies those choices using only generated CQRS interfaces and public arrangement
data. It has no EF, Models, Designers, Design or Generation runtime dependency. Internal model names
in generated repository names/diagnostic strings do not introduce model references.

## Optionality and boundaries

All testing settings are off by default. This example deliberately enables stores, reads, commands
and strict coverage. There is no new global write-mapping flag. Removing only `MapInMemoryWrites`
retains mapped reads and scripted writes; disabling commands emits no write adapter/factories.
Disabling automatic reads can retain the scalar helper for commands alone.

Full ordinary scalar coverage and direct same-type complete primary-key aliases are required. Creates
must assign their ordinary scalars or use explicit generated-value factories. Adapters never infer
an inverse DTO projection, fabricate constructor defaults or persist navigation graphs. Native/bulk
provider semantics, active EF filters, custom converters/comparers, constraints and concurrency races
still need explicit scripts or real-provider tests. See [the full contract](../../../docs/implemented/testing/INMEMORY-PROXY-WRITES.md).

## Run

```powershell
.\samples\ConfigurationExamples\Generate.ps1 -Configuration Release -Check -Run -ValidateCompilation
dotnet run --project samples/ConfigurationExamples/MemoryProxyWrites -c Release
```

Checked-in outputs make this consumer runnable directly. Use `Generate.ps1` without `-Check` to
regenerate deliberately; its bootstrap builds do not require pre-existing generated contracts.
