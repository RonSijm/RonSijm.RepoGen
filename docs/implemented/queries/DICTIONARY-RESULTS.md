# Designer-defined dictionary collision policies

Named dictionary queries now have an explicit design-time duplicate-key policy. Existing
`Dictionary(key)` / `Dictionary(key, value)` calls still throw on duplicates. Nothing changes
for simple generation, existing methods, or the attribute generator unless a policy is selected.

```csharp
entity.HasQuery("FirstByCategory")
    .Results<Item>()
    .OrderBy(item => item.Id)
    .Dictionary(item => item.Category,
        item => new ItemSummary(item.Id, item.Name),
        RepositoryDuplicateKeyBehavior.KeepFirst);

entity.HasQuery("LastByCategory")
    .Results<Item>()
    .OrderBy(item => item.Id)
    .Dictionary(item => item.Category,
        item => new ItemSummary(item.Id, item.Name),
        RepositoryDuplicateKeyBehavior.KeepLast);
```

The service calls a closed, named operation and receives
`IReadOnlyDictionary<string, ItemSummary>`. It does not choose a collision policy, expose an EF
query, group rows, or assemble a dictionary itself. These are ordinary result-builder terminals
after `Results`, projection, grouping, `From` or a registered provider root, and also work with
named compatibility/CQRS-selector queries.

## Policies and overloads

| `RepositoryDuplicateKeyBehavior` | Behavior |
| --- | --- |
| `Throw` | Reject duplicate keys; the unchanged default. Ordering is not required. |
| `KeepFirst` | Keep the first value encountered in the declared query order. |
| `KeepLast` | Replace a previous value when an equal key appears later in the declared query order. |

Both key-only and key/value overloads accept the policy:

```csharp
// Keep a whole approved DTO row as the value.
results.Dictionary(row => row.Category, RepositoryDuplicateKeyBehavior.KeepFirst);

// Select just the needed value; the default and explicit Throw are equivalent.
results.Dictionary(row => row.Category, row => row.Id);
results.Dictionary(row => row.Category, row => row.Id, RepositoryDuplicateKeyBehavior.Throw);
```

The policy is captured in the dictionary terminal. Replacing it with `Lookup(...)` or another
terminal does not carry first/last behavior forward. Invalid enum values (`RGD2501`), null
selectors and stale result builders fail before changing that terminal. There is no model-wide
default that silently turns collision failures into successful, lossy results.

Use `Lookup(key, value)` instead when **all** values for a key belong in the result. It returns
`IReadOnlyDictionary<TKey, IReadOnlyList<TValue>>` and retains its existing duplicate-preserving
semantics. First/last policies do not apply to lookups.

Optional [request-bound terminals](REQUEST-TERMINALS.md) let key/value selectors use the named
operation's typed input, either separately or together. Both request-aware mappings share the
key selector's input name. Collision and null-key rules still apply to the resulting runtime keys.

## Ordering and key semantics

First/last policies require ordering **guaranteed on every branch**. `RGD2502` rejects unordered
queries, conditional-only primary sorting and ordering discarded by grouping/distinct. Declare
an unconditional result `OrderBy` after replacement roots/grouping/distinct. An entity's enabled
default sort or explicit source sort is recognized for ordinary entity-root queries.

For an optional primary sort, put a fallback order first, then the optional sort and desired
tie-breakers. The generator's guard checks the presence of an order; it does not prove that a
key is unique. Designer authors must supply a unique tie-breaker where necessary. Declared order
inside an opaque registered provider or `From(...)` root does not by itself satisfy this guard.
See [Conditional query stages](CONDITIONAL-QUERY-STAGES.md) for branch/order rules.

Keys use `EqualityComparer<TKey>.Default`, just like existing dictionaries. This is CLR equality,
not the database's collation: string keys `"A"` and `"a"` remain distinct. Explicit typed-key
adapters and composite/nested key annotations work as before. Keep-last replaces the value;
it does not promise to replace the initially stored key object with a later equal key object.
The returned dictionary is a keyed result, not a sorted-list contract; no key enumeration order
is promised by `IReadOnlyDictionary`.

Null keys remain invalid under **every** policy. First/last do not silently discard a row with a
null key. Filter or map nullable source keys deliberately in the designer. Nullable values and
nested nullable key parts can still use [contract nullability](../repositories/CONTRACT-NULLABILITY.md). A
null-forgiving `!` annotation alone is not retained in an expression tree.

## SQL, limits and lifetime

The configured query still runs on the provider with its filters, joins, grouping, sorting,
projection and limits. The dictionary terminal selects its declared key/value pair and buffers
one result query, then assembles the dictionary in memory. A whole-row value selects that row's
configured shape; a scalar value selects the needed columns. No preliminary count or per-key
query is added. The new policy does **not** rewrite the query into a SQL window/group selection,
promise a database-side first/last-per-key optimization, or introduce a client-filter fallback.

`Skip`/`Take` and their conditional variants apply to source rows **before** dictionary assembly.
For example, `Take(100)` limits input rows, not distinct returned keys. Distinct key count can be
smaller. For very large duplicate sets, configure a provider-translatable grouped/joined selection
or an explicit registered query root instead of expecting dictionary assembly to prune SQL rows.
Constructor-based DTOs should remain at the final projection if further member access cannot
translate on the selected provider.

Visibility/global filters, source policies, independent tracking, shared transactions, context
leases and command timeouts retain their existing behavior. First/last assembly checks
cancellation before, between and after key/value insertions; a failure or cancellation never returns a
partial dictionary. Owned contexts dispose normally, and scoped timeouts restore before disposal.

## Caching, manifests and examples

Non-default policies participate in the closed cache shape fingerprint and the legacy named-query
cache identity. The same method/arguments configured to keep first cannot reuse a keep-last entry.
The default Throw fingerprint/output remains unchanged. Existing clone/invalidation behavior and
source dependencies remain in force; this is not a new cache backend or a comparer-serialization API.

Manifests add `dictionaryDuplicateKeys` only for non-default policies. Contracts and API snapshots
keep the same named method/input/result signatures when an existing operation changes policy;
source API snapshots do not claim to detect every behavior change.

The [Membership designer](../../../samples/ConfigurationExamples/Membership/Configuration/MembershipDirectoryRepositoryDesigner.cs)
adds explicitly ordered `FirstByCategory` and `LastByCategory` DTO operations to its application-owned
read port. Its [program](../../../samples/ConfigurationExamples/Membership/Program.cs) verifies distinct
selected values and confirms that hidden rows do not become the chosen last value. It still uses a
simple borrowed context, with no cache, tenancy or lease infrastructure requirement.
