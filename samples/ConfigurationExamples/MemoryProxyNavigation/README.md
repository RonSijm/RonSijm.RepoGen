# MemoryProxyNavigation: related DTOs without EF model dependencies

Start with [ProxyShelfRepositoryDesigner](Configuration/ProxyShelfRepositoryDesigner.cs).
The [EF model](Model/ProxyShelfContext.cs) owns the relationships; its entity classes are internal,
with a read-only collection and private navigation setters. The designer separately declares public
immutable arrangement rows, explicit scalar mappings and the relationships the fixture may load.
The [consumer](Program.cs) references only contracts, Testing and DI. It never references Models,
EF, Designers or Generation, and never constructs the persistence entities.

## Designer → exact output

| Declaration | Generated output / effect |
| --- | --- |
| Closed repositories + `BindReadPort<IShelfCatalog>()` | [EF-free Shelf contracts](../Contracts/Generated/ProxyShelfRepositories.g.cs), including the same named DTO operations |
| Three `HasInMemoryStore<TSeed, int>` declarations | [ProxyShelfContextRepositoryFixture](Generated/TestDoubles.g.cs) exposes typed public seed stores |
| Three `MapInMemoryEntity<TSeed>` scalar declarations | Internal plain read rows; persistence constructors/defaults/getters/setters are not invoked |
| `HasInMemoryNavigation(Items / Shelf / Category)` | Typed collection/reference slots on those read rows; no navigation-shaped seed objects |
| EF Code + Region alternate key / foreign key | Indexed composite-key loading; a null component never joins |
| `HasProjection<ShelfCard>` | Nested item/category DTOs; child filtering stays in the shared designer projection |
| `Detail.UseLoadProfile(Full)` | The explicit unfiltered Items → Category chain is rebuilt before projection |
| `Items.From(Set<ShelfItemRecord>...)` | Item-root visibility applies before result composition; related Shelf visibility is not injected |
| Page / Stream | One mapped participating-store snapshot per invocation or stream enumeration |

[Production output](Persistence/Repositories.g.cs) is checked in and independently compiler-validated.
The [manifest](Generated/manifest.json) lists scalar mappings, navigation slots, operation-specific
participants/keys and the explicit absence of entity construction, implicit related policies and writes.

The executable checks nested DTOs, optional/missing references, empty collections, null composite keys,
explicit child/root policies, a load profile, detached mutable DTOs, counted pages, cold streams and
coordinated baseline restore. No fake-specific selecting API is used in the consumer.

## The three shapes remain separate

- Internal persistence entities define production model/query semantics.
- Public immutable seeds contain scalar arrangement data, not EF entities or linked graphs.
- Public result DTOs are the application port's results; they are not fixture rows.

Scalar map assignments are declarations, not executed entity factories. Related mapped rows are
materialized once from a coordinated detached snapshot, then their explicitly selected relationship
slots are loaded. All referenced keys/scalars must be mapped: missing mappings require a typed script,
not invented defaults, including when the stores are empty.

Everything remains optional. `UseInMemoryTestQueries()` plus explicit stores/mappings/navigation
declarations enable this combination; removing a declaration or disabling the backend does not change
production/contracts/API/cache shapes. No extra global switch or service-side micro-extension is added.

This does not infer reverse mapping, graph writes, tracked identity, SQL collation, global EF filters,
AutoInclude, provider comparers, filtered includes or implicit/shared/unguarded optional-target behavior.
The separate [MemoryOptionalLinks](../MemoryOptionalLinks/README.md) designer shows the bounded
nullable-link extension without changing this example's ordinary relationship output.
[MemoryManyToMany](../MemoryManyToMany/README.md) separately demonstrates required explicit CLR join stores. See
[public mapped navigation semantics](../../../docs/implemented/testing/INMEMORY-PROXY-NAVIGATION.md) and
[real-provider contract fixtures](../../../docs/implemented/testing/REPOSITORY-CONTRACT-FIXTURES.md).

Run here: `dotnet run --project MemoryProxyNavigation.csproj`.
Regenerate/check with the [shared example workflow](../README.md#generate-check-and-run).
