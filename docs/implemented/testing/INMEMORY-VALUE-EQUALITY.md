# Optional fixture scalar equality

`HasInMemoryValueEquality(property, equality)` declares how a generated **tracked** fixture command
compares one ordinary scalar. This closes a bounded F15 value-policy gap: an explicit EF CLR
`ValueComparer` otherwise makes tracked writes require a script, even for a simple string field.

Start with [ComparisonNotesRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryScalarEquality/Configuration/ComparisonNotesRepositoryDesigner.cs)
and the [exact designer → output map](../../../samples/ConfigurationExamples/MemoryScalarEquality/README.md).

## Configure only when needed

```csharp
entity.HasInMemoryStore<NoteSeed, int>(row => row.Id, row => new(row.Id, row.Caption, row.Revision))
    .MapInMemoryEntity<NoteSeed>(row => new StoredNote {
        Id = row.Id, Caption = row.Caption, Revision = row.Revision })
    .MapInMemoryWrites<NoteSeed>((row, saved) => new(row.Id, row.Caption, row.Revision))
    .HasInMemoryValueEquality(row => row.Caption,
        (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
    .HasInMemoryValueEquality(row => row.Revision,
        (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase));
```

An entity-shaped public store also works. A mapped public store needs its explicit write map first.
Automatic test commands must be enabled; declarations do not enable any backend. No equality
policies are inferred from EF metadata, property names or database collation, and none are configured
by default. `WithoutInMemoryValueEquality(property)` removes one policy. Replacing/removing a store
also removes its policies. Disabled automatic commands emit no helper or receipt for these declarations.

## What the policy affects

- Scalar state comparison during tracked single/bounded-batch/child/link Stage/Save preparation.
- Expected-version comparison for tracked patches/deletes/batches and reconciled child/link rows.
- Original-versus-current token comparison during shared save preparation.
- Whether a non-forced, equivalent staged assignment needs a saved-row change.

An equivalent non-forced assignment can save zero rows and retain the original stored representation.
`ReturnsSaved` then reads that stored representation. An expected-version patch still forces its
conditional write even when assigned values compare equal. In a non-forced mixed patch, only genuinely
different configured fields are merged; an equivalent field does not lose its saved representation
just because another field changed. The same rule survives reused pending entries and batches/children.
Existing missing/conflict/visibility,
atomic preparation, shared-save counts, scripted overrides, cancellation, baseline and checkpoint
semantics remain unchanged; the policy is not a stale-client retry.

Generated helpers contain typed, self-contained expressions. Generation does not compile or invoke
the expression, evaluate captured getters, or execute EF's configured comparer. Both inputs must
remain referenced. Captures, ambient clocks/static state, arbitrary helpers/graphs/delegates and
custom operators outside the portable framework subset are rejected. Authors remain responsible
for purity and a genuine equivalence relation; generation cannot prove reflexivity/transitivity or
database equivalence for an arbitrary expression.

## Equality is not one universal switch

| Behavior | Equality used |
| --- | --- |
| Tracked scalar change/version comparison | Declared scalar policy; ordinary structural equality for other fields |
| Copy and public write-map round-trip validation | Ordinary structural equality, even when a scalar policy is declared |
| Store duplicate/Find/Remove/pending identity | Separate [fixture-key comparer](INMEMORY-KEY-COMPARERS.md) |
| Query predicates, sorting, membership, grouping and dictionaries | Their designer expressions and ordinary supported CLR execution |
| Owner/FK matching, saved-result primary keys and collection matching keys | Existing structural/key contracts; not changed by this policy. Read navigation loading has a separate [principal-component policy](INMEMORY-RELATIONSHIP-COMPARERS.md) |
| Immediate/native update/delete/insert/upsert and SQL predicates | Existing explicit simulation/provider boundaries; not enabled or rewritten |
| Production EF CLR comparison / SQL collation | Actual EF model/provider configuration, never rewritten |

Keeping round-trip validation separate matters: a case-insensitive policy must not conceal a writer
or copier that changes `mixed` to `MIXED` while persisting a deliberately case-preserving assignment.
Likewise, a case-insensitive CLR comparer does not make a query's `row.Caption == caption`
case-insensitive SQL or LINQ-to-Objects. Configure query semantics separately when that is intended.

## Bounded support and diagnostics

Select one ordinary mapped primitive/enum/string/decimal/Guid/date/time/binary scalar. This slice
excludes primary/alternate keys, unique-index members, foreign keys, generated scalars and binary
concurrency tokens. Their identity/relationship/sentinel/rowversion contracts must not be loosened.
Public scalar maps, immutable/protected assignments, selected stores and graph coverage still apply.

A declaration can explicitly acknowledge compatible CLR value-comparer metadata for supported
tracked fields. It cannot acknowledge a value converter, provider comparer, inconsistent/unknown
model metadata, SQL comparison, inheritance, ownership or arbitrary value-object behavior.
Immediate commands on such custom-comparer models still require their own typed script/real provider.

- RGD4101: missing/mismatched store, duplicate policy or unsupported mapped scalar/identity shape.
- RGD4102: captured/nonportable equality, or an input that is not referenced.
- Existing RGD3402: remaining command behavior still needs `RequireTestOverride(reason)` in strict mode.

Manifests add `valueEqualities` only to configured fixture stores: property/expression, tracked-only
scope, unchanged production/query/key/relationship semantics, strict round trips and no provider
equivalence. Production APIs/source/cache fingerprints stay unchanged.

Shared cases run the actual generated implementation on SQLite with explicit CLR comparers and a
separately declared `NOCASE` token collation. They verify selected ASCII state/version cases, not
all Unicode/collation/provider behavior. Use [real-provider fixtures](REPOSITORY-CONTRACT-FIXTURES.md)
for actual SQL constraints, rowversions, locking and race guarantees.
