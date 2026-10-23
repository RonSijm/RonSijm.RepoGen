# MemoryPartitions: typed context keys and public storage proxies

Start with [PartitionDirectoryRepositoryDesigner](Configuration/PartitionDirectoryRepositoryDesigner.cs).
It keeps a database entity out of the generated testing dependency graph, using an explicit public
SettingSeed store. Typed context keys partition fixture data; singleton sharing across request scopes
is an explicit test choice, not a generator default.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs this designer with
[PartitionDirectoryContext](Model/PartitionDirectoryContext.cs) and writes these four artifacts:

| Designer declaration | Exact generated result |
| --- | --- |
| `BindReadPort<ISettingLookup>()` + closed repositories | [IReadPartitionSettingRepository / combined port](../Contracts/Generated/MemoryPartitionsRepositories.g.cs), inheriting the application lookup port |
| `UseScriptedTestDoubles(...)` | [PartitionSettingRepositoryTestDouble](Generated/TestDoubles.g.cs), typed OnFindAsync and explicit scoped aliases |
| `UseInMemoryTestStores()` | [PartitionDirectoryContextRepositoryFixture / RepositoryData](Generated/TestDoubles.g.cs), injected into each double |
| `UseKeyedContextLeases<PartitionKey>()` | Fixture.For(PartitionKey) and ContextKeys, rather than a fixed Data property |
| `HasInMemoryStore<SettingSeed, string>(...)` | PartitionSettingStore contains public SettingSeed rows, **not** the database's PartitionSetting type |
| `HasQuery("Find").UseContextKey(...).ProjectTo<SettingView>()` | [Production keyed EF operation](Persistence/Repositories.g.cs); the script still explicitly implements test behavior |
| Storage configuration | [Manifest](Generated/manifest.json): ExplicitDataShape, key/copy sources and RequiresOverride operation |

[Contracts/PartitionContracts.cs](Contracts/PartitionContracts.cs) contains immutable context keys,
requests/DTOs, the public storage shape and the application port. The persistence model lives only
in [Model](Model/PartitionDirectoryContext.cs). The [consumer project](MemoryPartitions.csproj)
references Contracts, Testing and DI, excluding EF/Models/Designers/Generation and production output.
Independent compiler validation checks that boundary with actual .NET 8 references.

## What Program.cs proves

[Program.cs](Program.cs) explicitly registers one singleton fixture **before** test registration.
Two request scopes receive different script instances but the same data fixture. Two context keys
can seed the same row key without leaking values; equal context keys retain stable store identities.
Baseline capture/restore affects only one selected partition. Resetting one double's scripts affects
neither shared data nor the other scope's arrangements.

Partition selection in the test handler is explicit. The store foundation does not evaluate
UseContextKey, Where, projections, tenancy authorization or relational behavior automatically.
The public storage proxy adapts storage, not arbitrary production expressions. Missing scripts
remain strict unexpected-call failures.

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Check -Run -ValidateCompilation
dotnet run --project samples/ConfigurationExamples/MemoryPartitions -c Release
```

Read [in-memory fixtures](../../../docs/implemented/testing/INMEMORY-FIXTURES.md) for immutable keys and baseline/copy
requirements. [MemoryFixtures](../MemoryFixtures/README.md) is the simpler scoped, non-keyed setup.
