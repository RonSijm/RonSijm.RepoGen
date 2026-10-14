# Optional composite child matching keys

An explicitly designed `ReconcileCollection` can match each leaf child by a combination of
columns instead of one scalar. The service still calls one named operation with an application
request; it does not construct matching predicates or use Entity Framework APIs.

Start with [TranslationCatalogRepositoryDesigner](../../../samples/ConfigurationExamples/CompositeCollections/Configuration/TranslationCatalogRepositoryDesigner.cs)
and its [designer → exact output walkthrough](../../../samples/ConfigurationExamples/CompositeCollections/README.md).
This extends the existing [production reconciliation](COLLECTION-RECONCILIATION.md) and optional
[fixture reconciliation](../testing/INMEMORY-COLLECTIONS.md), not their supported graph shapes.

## Design the whole matching key

```csharp
entity.HasCommand("SaveTranslations")
    .Where<TranslationChange>((document, request) =>
        document.TenantId == request.TenantId && document.Id == request.Id)
    .ReconcileCollection<LocalizedText, TranslationInput, (string, string), TranslationChange>(
        document => document.Texts, request => request.Texts,
        child => new ValueTuple<string, string>(child.Code, child.Locale),
        item => new ValueTuple<string, string>(item.Code, item.Locale),
        children => children.MaximumItems(100)
            .Add(item => new LocalizedText
                { Code = item.Code, Locale = item.Locale, Text = item.Text })
            .Update(setters => setters.SetProperty(
                child => child.Text, (child, item) => item.Text))
            .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
    .Save();
```

The EF model must prove uniqueness—for example, an unfiltered unique index on
`(TenantId, DocumentId, Code, Locale)`. A global unique key/index or a stricter unique subset of
the ownership plus matching columns also suffices, provided it includes at least one matching column.
Ownership-only uniqueness does not opt a collection into singleton-child semantics. An index containing
additional unmatched columns, a non-unique index, or a filtered unique index does not establish this guarantee.

Tuple literals such as `(child.Code, child.Locale)` cannot be used in C# expression trees.
Use an explicit `new ValueTuple<...>(...)` constructor in the child selector. It must have two to
seven distinct direct mapped scalar components, in the intended matching order. The item selector
returns the same tuple type and component order; it may construct the tuple from item properties
or return an existing tuple property. RepoGen does not execute a designer lambda during generation.

Each mapped child component must be non-null, readable, non-array, and not a foreign key.
If Add is configured, every matching component must be application-supplied, not store-generated,
and explicitly assigned by its scalar initializer. No matching component may be changed by Update,
even with `AllowWrite`. Ownership foreign keys always come from the selected parent, never the item.
These checks use RGD2801/RGD2802; arbitrary key constructors, nested/rest tuples, computed child
selectors, duplicate columns and converted child selectors are not implicitly adapted. Nested tuple
columns remain unsupported even behind a scalar converter or a required nullable CLR wrapper.

## Execution semantics

| Concern | Behavior |
| --- | --- |
| Matching | Default CLR `ValueTuple` equality across the complete ordered combination |
| Equal prefixes | `("title", "en")` and `("title", "nl")` are different matching keys |
| Ownership | The same combination under another owner is outside this command's mutation scope |
| Visibility | Only the selected owner's visible children are candidates; omitted hidden rows are not deleted |
| Input | A bounded snapshot is prepared once; duplicate combinations and any null component fail before context acquisition or fixture selection |
| Invalid saved rows | Duplicate/null matching components fail before child setters or membership changes; command-assigned parent scalars are restored |
| Missing items | Keep remains the default; Reject and Delete remain explicit choices |
| Stage/Save | Stage does not persist; Save is the existing shared unit-of-work save, not an entity-specific commit |
| Versions/results/rollback | Existing expected-version, result and explicit transaction/checkpoint rules are retained |

Generated typed component checks evaluate the key expression once and return the same tuple.
They use no reflection, dynamic SQL identifiers, array construction or custom comparison injection.
Scalar-key commands do not emit this composite infrastructure or new receipt fields.

The manifest adds `compositeMatchingKey` only to configured composite collections. It lists ordered
property names, `DefaultClrValueTuple` equality, `RejectBeforeMutation` null handling, owner/visible
scope and immutability. Existing collection bounds, policies, versions and child cache tags remain
inspectable alongside it.

## Optional fixture backend and boundaries

`SimulateCollectionWrites()` opts a suitable designed command into the fixture backend. It reuses
the same matching delegates with selected parent/child stores and existing entity-shaped or public
scalar read/write maps. Unconfigured commands remain scripted; no new global switch, dependency or
service-side collection DSL is required. The example combines composite ownership and matching,
internal persistence classes, public immutable arrangement rows, explicit fixture IDs and versions.

SQL collation/conversions may differ from CLR equality and may reject a save that looks distinct
in memory. Fixture identity comparers do not rewrite matching equality. Provider conversions or
custom comparers retain explicit fixture override coverage; SQL unique/FK constraints, concurrent
writers, rowversion, triggers and isolation are not simulated. The actual generated production and
fixture implementations have shared SQLite contract tests; this is not a SQL Server equivalence claim.

Owned/nested/many-to-many mutation, composite foreign-key input binding, automatic unlinks/soft
deletes and custom key behavior are not inferred. Complete ownership binding from EF metadata is
supported; accepting arbitrary ownership values from the request is deliberately not added.
