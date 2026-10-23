# Typed mapped-property lookups

Start with [AliasDirectoryRepositoryDesigner](Configuration/AliasDirectoryRepositoryDesigner.cs).
It explicitly selects two EF scalar properties that have no members on `AliasEntry`. Services
call typed methods, never pass EF property names or construct predicates.

## Designer → generated output

The [generation runner](../GenerationRunner/Program.cs) pairs this designer with
[AliasDirectoryContext](Model/AliasDirectoryContext.cs) and writes these exact artifacts:

| Declaration | Generated output | Effect |
| --- | --- | --- |
| `UseCqrsSelectors()` and `WriteEnabled = false` | [Repositories.g.cs](Generated/Repositories.g.cs) | `IReadAliasEntryRepository`, detached read selectors and `AddAliasDirectoryContextSelectorRepositories` |
| `HasAccessor<string>("SearchCode")` | [Repositories.g.cs](Generated/Repositories.g.cs) | `BySearchCode(string searchCode)` returns a single selector, using the model's unique index |
| `HasAccessor<AliasState>("State").UseValueConverter<string>()` | [Repositories.g.cs](Generated/Repositories.g.cs) | `ByState(string state)` returns a collection; the existing EF converter supplies the model enum |
| `HasProjection(...)` and `ProjectionEnabled = false` | [Queries.g.cs](Generated/Queries.g.cs) | Only the designed `ProjectToAliasCard()` DTO mapping is exposed |
| Explicit mapped-property declarations | [manifest.json](Generated/manifest.json) | Exact model names/types, shadow status and lookup uniqueness under `mappedPropertyBindings` |

The important declarations are:

```csharp
entity.HasAccessor<string>("SearchCode");
entity.HasAccessor<AliasState>("State")
    .UseValueConverter<string>().HasMethodName("ByState").IsUnique(false);
```

The generic argument is the EF **model type**, before input adaptation. Unknown names and
mismatched types fail generation. Other shadow keys/indexes do not become accessors implicitly.

The [consumer](Program.cs) uses the emitted API directly:

```csharp
var entry = await reads.BySearchCode("alpha").ProjectToAliasCard();
var active = await reads.ByState("Enabled").ProjectToAliasCard();
```

These are CQRS **selectors**, not closed EF-free repository ports. For service contracts with
only named request/result methods, see [ShadowVersions](../ShadowVersions/README.md). Both
examples keep design-time property names out of the consumer's method arguments.

## What the consumer verifies

`Program.cs` creates a fresh SQLite database, seeds shadow state in test setup, and checks unique
and collection lookup shapes, reverse conversion, configured DTOs, the shadow `Archived` global
filter, detached reads and parameterized injection-shaped input. It does not execute the designer.

Run `dotnet run --project samples/ConfigurationExamples/MappedAccessors -c Release` from the
repository root. After changing inputs, regenerate using
`./samples/ConfigurationExamples/Generate.ps1 -Configuration Release`.

No resolver, cache, write API or generated fixture is needed. The shared Models, Contracts and
Designers build-support projects compile the colocated inputs; the consumer does not reference
Designers. See [mapped-property bindings](../../../docs/implemented/repositories/MAPPED-PROPERTY-BINDINGS.md)
for exact naming, type and fixture boundaries.

[Example index](../README.md)
