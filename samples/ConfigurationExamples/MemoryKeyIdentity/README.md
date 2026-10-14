# MemoryKeyIdentity: explicit fixture keys, unchanged query semantics

Start with [KeyDirectoryRepositoryDesigner](Configuration/KeyDirectoryRepositoryDesigner.cs).
The only new choice is `HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase)`.
The [Program](Program.cs) consumes the generated read/write interfaces and arranges public rows;
it does not generate repositories or reference the internal persistence model.

## This designer produces these files

| Designer declaration | Exact generated output | What to inspect |
| --- | --- | --- |
| Closed Find/StageCreate/Rename/Remove operations | [Contracts/Generated/KeyDirectoryRepositories.g.cs](../Contracts/Generated/KeyDirectoryRepositories.g.cs) | EF-free CQRS interfaces and public request/DTO types |
| Ordinary predicates and approved scalar writes | [Persistence/Repositories.g.cs](Persistence/Repositories.g.cs) | Production EF operations; comparer configuration does not change them |
| Public store, scalar adapters and explicit comparer | [Generated/TestDoubles.g.cs](Generated/TestDoubles.g.cs) | CreateStore's keyComparer argument and mapped shared-write registration |
| Configured fixture identity and operation coverage | [Generated/manifest.json](Generated/manifest.json) | FixtureKeyIdentityOnly; query/relationship/context-key comparison unchanged |

[GenerationRunner](../GenerationRunner/Program.cs) explicitly pairs `KeyDirectoryContext` with this
designer and the four destinations. The shared Models/Contracts/Designers projects compile the
adjacent handwritten inputs; those support projects are not separate configurations.

## What changes—and what does not

Fixture `Find("abc")`, duplicate seed policies, staged-key reservations, saves, baseline resets and
checkpoint rollback all use the configured comparer. `"ABC"` and `"abc"` are one **fixture key**.
The public immutable [EntrySeed](Contracts/EntrySeed.cs) is separately mapped to scalar read/write
slots; no persistence constructor or inverse DTO projection is invoked.

The designed `FindAsync(key)` still evaluates `row.Code == key`. Consequently `FindAsync("abc")`
does not match a row whose Code is `"ABC"` in the LINQ-to-Objects fake, while fixture arrangement
`store.Find("abc")` does. A command's predicate also keeps its original meaning. This distinction is
intentional: key identity is not a request to rewrite filters, sorting, joins or relationship keys.
The executable verifies both behaviors, duplicate rejection and restore paths.

SQL collation and race/constraint guarantees need real-provider tests. This example does **not**
claim fake/SQL equivalence for case-sensitive or case-insensitive comparisons.

## Default and customization

Omit the comparer line (or call `WithoutInMemoryKeyComparer()`) for `EqualityComparer<string>.Default`.
No factory or receipt is emitted in that case. Testing itself remains disabled by default. An explicit
factory executes once when each selected fixture store/context partition is created, never at generation.

For a composite key, explicitly construct a public `IEqualityComparer<TKey>` in an independent shared
contract assembly. Captured instances, ambient/culture factories, private/model types and null factories
are rejected with RGD3204. The comparer must keep immutable, deterministic equality/hash behavior for
the fixture lifetime. It does not change the comparer used to select context partitions.

See [fixture-key comparers](../../../docs/implemented/testing/INMEMORY-KEY-COMPARERS.md) for the complete boundary.

## Run

```powershell
.\samples\ConfigurationExamples\Generate.ps1 -Configuration Release -Check -Run -ValidateCompilation
dotnet run --project samples/ConfigurationExamples/MemoryKeyIdentity -c Release
```

Checked-in outputs make the consumer runnable directly. Regenerate deliberately by omitting `-Check`.
This executable has no EF, model, designer or generator runtime dependency.
