# Optional fixture-key comparers

For tracked **non-key scalar** state/version comparison, see the separate
[HasInMemoryValueEquality option](INMEMORY-VALUE-EQUALITY.md). It does not replace this key-identity policy.

`HasInMemoryKeyComparer<TKey>` configures **test-store key identity**, not query equality or database
collation. It is an opt-in RepositoryDesigner declaration on an existing `HasInMemoryStore`.
Ordinary generation and production contracts/queries are unchanged.

## Configure in the designer

```csharp
modelBuilder.Entity<DictionaryEntry>(entity =>
{
    entity.HasInMemoryStore<EntrySeed, string>(row => row.Key,
        row => new EntrySeed(row.Key, row.Caption))
        .HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase);
    // Declare production queries and optional scalar read/write maps normally.
});
```

`TKey` must exactly match the preceding store's key type. Entity-shaped and explicitly public-data
stores both support this setting. Automatic reads/writes are separate choices; scripts-only and
read-only stores can use comparers too. All testing generation remains off by default.

Omit the declaration, or call `WithoutInMemoryKeyComparer()`, to retain
`EqualityComparer<TKey>.Default`. No extra factory argument or comparer receipt is emitted in that
case. Replacing/removing the store removes its comparer. Removing only a read/write map does not.
Disabled/unselected stores do not validate or emit the unused factory.

## Construction boundary

The factory is inspected and emitted, **never compiled/invoked at generation**. Its expression is
evaluated once when each selected store is constructed, including once per context partition. Repeated
lookups, saves and resets reuse that comparer; they do not construct another instance.

Accepted declarations are self-contained constructor expressions and these explicit built-in choices:

- `StringComparer.Ordinal` and `StringComparer.OrdinalIgnoreCase` for string keys.
- `EqualityComparer<TKey>.Default` for the exact key type.
- `new MyPublicComparer(...)`, implementing `IEqualityComparer<TKey>`, with portable literal,
  allowed built-in-comparer or public-constructor arguments. Composite keys are supported explicitly.

Place custom comparers in an independent public contract/test-data assembly referenced by the testing
consumer and optional `testDoubleReferences`. Public construction must not mention persistence entity,
private, EF or designer types. Captured comparer instances/objects, delegate calls, arbitrary methods,
static factories/getters, ambient culture choices, null factories and object-cast escapes are rejected
with **RGD3204**. No designer state is evaluated to recover a comparer instance.

For example, an independent composite-key comparer can be declared in shared test contracts:

```csharp
public sealed class DirectoryKeyComparer : IEqualityComparer<(string Code, int Slot)>
{
    public bool Equals((string Code, int Slot) x, (string Code, int Slot) y) =>
        StringComparer.OrdinalIgnoreCase.Equals(x.Code, y.Code) && x.Slot == y.Slot;
    public int GetHashCode((string Code, int Slot) key) =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Code), key.Slot);
}

// Following a store whose key type is ValueTuple<string, int>:
entity.HasInMemoryKeyComparer<ValueTuple<string, int>>(() => new DirectoryKeyComparer());
```

RepoGen does not inspect the implementation of custom constructors or equality/hash methods.
Consumers must keep them deterministic, thread-safe, mutually consistent and stable for the store's
lifetime, with immutable keys. They must not depend on clocks/ambient culture/services, mutate fixtures or
re-enter arrangement/write operations. A source-portability check cannot prove comparator correctness.

## Exactly which behavior uses it?

| Behavior | Equality used |
| --- | --- |
| Seed/SeedRange/ReplaceWith duplicates; Ignore/Replace/Throw | Configured store-key comparer |
| Fixture Find/Remove; copy-key identity checks | Configured store-key comparer |
| Pending-key reservations, duplicate creates and saved dictionaries | Configured store-key comparer, including mapped public write views |
| Baselines, snapshots and checkpoint restores | Preserve that store's comparer-backed identity |
| Designer Where, visibility, sorting, joins, dictionary terminals and navigation relationships | Existing declared CLR/LINQ behavior; not rewritten |
| Batch input matching and scalar/version/round-trip validation | Existing declared matching/scalar behavior; not rewritten |
| Fixture.For(contextKey) | Existing context-partition comparer; not changed by an entity-store setting |
| Production EF queries, uniqueness/constraints and collation | Provider/model behavior; not changed |

Therefore `store.Find("abc")` can find a seeded `"ABC"` while a designed operation containing
`row.Code == key` returns no match for `key = "abc"` in the fake. Commands still select rows using
their predicates; the comparer does not turn an ordinary equality filter into a case-insensitive one.
Adapters must still round-trip every scalar; key equivalence does not license lossy key rewriting.

Use a declared portable predicate if case-insensitive application behavior is genuinely intended,
and verify its translation with the selected provider. Configuring fixture identity is not a substitute
for testing SQL collation, uniqueness, concurrency or transaction isolation.

## Receipt, example and verification

Only explicitly configured stores add a `testDoubles.stores.entities[].keyComparer` receipt. It
records the emitted source, `FixtureKeyIdentityOnly` scope and per-store/partition construction,
with query/relationship/context-key changes and relational equivalence explicitly false.

[MemoryKeyIdentity](../../../samples/ConfigurationExamples/MemoryKeyIdentity/README.md) shows its
[KeyDirectoryRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryKeyIdentity/Configuration/KeyDirectoryRepositoryDesigner.cs)
and four exact generated artifacts. The EF-free consumer verifies unchanged predicates, duplicate
seed policies, pending/save identity, baseline restore and checkpoint rollback with mapped public rows.

Automated cases also cover all output layouts, default-output stability, production-contract stability,
prerequisites/removal, invalid/unused factories, explicit defaults, custom construction timing,
composite keys, save-time duplicate atomicity and independently case-sensitive context partitions.

Related: [fixture storage](INMEMORY-FIXTURES.md), [stateful writes](INMEMORY-WRITES.md),
[public write adapters](INMEMORY-PROXY-WRITES.md), [portable reads](INMEMORY-QUERIES.md),
and the independently configured [relationship-key comparison](INMEMORY-RELATIONSHIP-COMPARERS.md).
