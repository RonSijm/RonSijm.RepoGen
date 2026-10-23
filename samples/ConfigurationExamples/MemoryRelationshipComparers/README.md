# MemoryRelationshipComparers: one compared principal-key component

Start with [ComparedDirectoryRepositoryDesigner](Configuration/ComparedDirectoryRepositoryDesigner.cs).
The [context](Model/ComparedDirectoryContext.cs) maps a `(Region, Code)` alternate key and an optional
entry-to-folder reference. The designer explicitly selects a fixture comparer for Code; Region
remains exact. Internal models and read-only navigations are never constructed by the consumer.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/ComparedDirectoryRepositories.g.cs) | Two handwritten read ports and scalar/DTO result types |
| [Production repositories](Persistence/Repositories.g.cs) | Original designer predicates/projection; no fixture comparer injected |
| [Fixtures](Generated/TestDoubles.g.cs) | Compared complete keys for both reference and collection loaders |
| [Manifest](Generated/manifest.json) | Fixture relationship/uniqueness scope and unchanged query/store/write equality |

| Designer declaration | Generated effect |
| --- | --- |
| HasInMemoryRelationshipComparer(folder.Code, OrdinalIgnoreCase) | Both traversal directions match differently cased FK codes |
| Complete Region/Code relationship | Same Code in another Region stays separate |
| HasInMemoryNavigation on each end | Only selected/accessed edges are rebuilt from coordinated snapshots |
| Public scalar seed/maps | EF/model-free consumer with immutable arrangement rows |
| Named summary projection | Detached list and cold stream use the same designer mapping |
| ByExactCode with == | Query predicate remains ordinal in fixtures; comparison does not rewrite queries |

The [consumer](Program.cs) checks both directions, null FKs, unchanged predicates, detached DTOs,
cold snapshot-stable streams, alternate-key uniqueness across different primary IDs and baseline reset.
It references Contracts, Testing/Abstractions and DI, not EF, Models, Designers or generator tooling.

The policy defaults to absent. It does not replace [store identity](../MemoryKeyIdentity/README.md)
or [tracked scalar equality](../MemoryScalarEquality/README.md), nor infer SQL collation, graph writes,
authorization or arbitrary value-object/provider behavior. See [semantics and limits](../../../docs/implemented/testing/INMEMORY-RELATIONSHIP-COMPARERS.md).

Run: `dotnet run --project MemoryRelationshipComparers.csproj`.
Regenerate/check with the [shared workflow](../README.md#generate-check-and-run).
