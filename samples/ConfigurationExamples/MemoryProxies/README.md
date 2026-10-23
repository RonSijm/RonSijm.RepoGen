# MemoryProxies: public fixture data for an internal EF model

Start with [ProxyDirectoryRepositoryDesigner](Configuration/ProxyDirectoryRepositoryDesigner.cs).
The persistence [DirectoryRecord](Model/DirectoryRecord.cs) is **internal** and lives only in the
Models assembly. Arrangement [DirectorySeed](Contracts/DirectorySeed.cs) and result
[DirectoryCard](Contracts/DirectoryCard.cs) are distinct public contract types. The
[consumer](Program.cs) references Contracts and Testing, not Models, Designers, Generation or EF.

## Designer → generated output

The [runner](../GenerationRunner/Program.cs) pairs ProxyDirectoryContext with this designer.

| Declaration | Exact output |
| --- | --- |
| UseClosedRepositories + BindReadPort<IProxyDirectory> | [IReadDirectoryRecordRepository : IProxyDirectory](../Contracts/Generated/ProxyDirectoryRepositories.g.cs); no persistence entity in signatures |
| HasInMemoryStore<DirectorySeed, int>(key, copy) | [ProxyDirectoryContextRepositoryData.DirectoryRecordStore](Generated/TestDoubles.g.cs), storing public seed rows |
| MapInMemoryEntity<DirectorySeed>(initializer) | [Internal __RepoGenDirectoryRecordReadRow and scalar adapter](Generated/TestDoubles.g.cs); Key → Id, Label → Name, Credits × 10 → Points, Archived → Disabled |
| Visibility + default sort + named projection | FindAsync, PageAsync and StreamAsync reuse the designer definition over mapped scalars |
| Results<DirectoryRecord>().Sum(...) | TotalPointsAsync reads mapped Points; no public EF/entity selector |
| Unmapped InternalTag + RequireTestOverride | InternalTagsAsync remains a typed script with a missing-member reason in [manifest](Generated/manifest.json) |
| UseScriptedTestDoubles + optional stores/queries | AddProxyDirectoryContextRepositoryTestDoubles registration and shared fixture; production registration is untouched |

[Production output](Persistence/Repositories.g.cs) is checked in and compiler-validated separately.
It continues to query the real internal entity. It is not compiled into this EF-free executable.
The sample model grants access to the separate designer/compiler-validation assemblies in
[its infrastructure-only assembly attributes](Model/ProxyDirectoryAssemblyInfo.cs). No such access
is required by the generated test doubles or consuming application.

## Explicit, optional adaptation

The mapping initializer declares **scalar assignments**, not a factory that will instantiate the
entity. No entity constructor, setter, navigation, inheritance or change-tracker behavior runs in
the fake. Members omitted from the mapping do not receive fabricated defaults: reads that depend
on them require an explicit script. Constructor arguments, navigation/computed members and
non-portable mapping expressions produce RGD3203. Referenced unmapped query/policy members are
reported as RequiresOverride (RGD3302 if strict and unacknowledged).

Remove MapInMemoryEntity or call WithoutInMemoryEntityMapping to retain proxy storage with
script-only reads. UseInMemoryTestQueries(false) disables the entire automatic backend. All
testing options are off by default; production query/API/cache shapes are unchanged. Writes are
not reverse-mapped or simulated. Pure value transformations use .NET semantics, not SQL semantics.

Unit tests also cover two mapped private roots in joins/left joins/anti-joins, scalar unions,
grouped projections, dictionaries, dynamic field sorting, cursors and context partitions.
See [portable read boundaries](../../../docs/implemented/testing/INMEMORY-QUERIES.md).

For related rows, [MemoryProxyNavigation](../MemoryProxyNavigation/README.md) combines these scalar
maps with separately declared navigation slots. No navigation is assigned inside the initializer;
its internal/read-only persistence models are never constructed by the fixture.

Run from this folder: `dotnet run --project MemoryProxies.csproj`.
Regenerate/check using the [shared example workflow](../README.md#generate-check-and-run).
