# Contract nullability in the designer

Reference nullability is a contract promise, not a different CLR type. Expression trees cannot
distinguish `string[]` from `string?[]`, or `List<string>` from `List<string?>`. RepoGen therefore
keeps its existing non-null reference defaults and provides explicit, optional annotation trees.
No new configuration is required for ordinary CRUD or DTOs with already-declared nullable members.

## Nullable collection inputs

Configure the input's outer node and its children independently:

```csharp
entity.HasQuery("GetAuthorNames")
    .InputNullability<IReadOnlyList<string>>("titles", type => type
        .Nullable()
        .TypeArgument(0, item => item.Nullable()))
    .Where<IReadOnlyList<string?>>((book, titles) =>
        titles == null || titles.Contains(book.Title))
    .OrderBy(book => book.Id)
    .Select(book => book.Author == null ? null : book.Author.Name)
    .ResultNullability<string>(type => type.Nullable())
    .List();
```

The closed operation has this application-facing signature:

```csharp
Task<IReadOnlyList<string?>> GetAuthorNamesAsync(
    IReadOnlyList<string?>? titles,
    CancellationToken cancellationToken = default);
```

The predicate deliberately chooses **null input means all rows** and **empty input means no rows**.
The annotation alone does neither: it does not insert null checks, validation, or filter behavior.
Use reviewed predicates or `Validate(...)` to choose those semantics. Nullable input elements do
not imply that an SQL provider treats `NULL` as equal to another `NULL`.

`AllowNullInput<T>(name)` remains the shorthand for only the outer nullable reference. Use either
that shorthand or one `InputNullability<T>(name, ...)` tree per parameter, not both for the same
input. The name and entire CLR type must match an input extracted from the operation's clauses.
Both query builder forms and command builders support `InputNullability`.

## Arrays and nested generic types

`Nullable()` affects the current reference node; children stay non-null unless configured.
`Element(...)` is for arrays and `TypeArgument(index, ...)` uses zero-based CLR generic arguments.

```csharp
// string?[]? : nullable array, nullable strings
.InputNullability<string[]>("names", type => type.Nullable()
    .Element(item => item.Nullable()))

// string?[]?[] : non-null outer array, nullable inner arrays and strings
.InputNullability<string[][]>("names", type => type
    .Element(array => array.Nullable().Element(item => item.Nullable())))

// IReadOnlyDictionary<string, IReadOnlyList<string?>?>?
.InputNullability<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
    "mapping", type => type.Nullable()
        .TypeArgument(1, list => list.Nullable()
            .TypeArgument(0, item => item.Nullable())))
```

Rectangular array rank is retained, including `string?[,]?`. Nested declaring types are formatted
as `Outer<T>.Inner<U>`; their CLR argument list includes the outer arguments first.
Nullable value types remain part of the actual CLR type: declare `int?[]`, not `int[]` plus a
reference annotation. For `Optional<IReadOnlyList<string>>`, configure its reference argument
while leaving the outer value type unchanged. Builders are snapshotted when registered; keeping
and mutating a callback's builder later does not alter the registered design.

For portable SQL array predicates use `Enumerable.Contains(values, value)` explicitly. Newer
framework overload resolution can select span-based array extensions; general span expressions
are not a RepoGen SQL-query feature. Normal LINQ calls infer reference annotations when their
CLR generic arguments can be inferred exactly; explicit widening and type-only arguments remain.

## Result values and reusable projections

`ResultNullability<TValue>(...)` must match the operation's final output **value** type:

- List, group, page, slice, cursor page, and stream: each final row/item.
- Single/optional/first: the selected value. Missing-row optionality remains in addition to that shape.
- Dictionary/lookup: each dictionary or lookup value, not the key or dictionary container.
- Min/Max/Sum/Average: the aggregate value.
- A command with `Returns(...)`: the saved value inside `CommandResult<TValue>`.

Task, list/page/dictionary containers remain non-null. A successful command may have
`HasValue == true` with a deliberately nullable saved value; that is different from an absent result.
Dictionary keys retain their non-null outer contract. Optional `KeyNullability<TKey>(...)` annotates
their generic/array children independently from values. These options do not configure DTO member
declarations, invent SQL translations, change CLR value types, or bypass C# generic constraints.

Declare a generic named projection's annotations once:

```csharp
entity.HasProjection("ProjectToBox", row => new Box<string?>
    { Id = row.Id, Value = row.Description })
    .ResultNullability(type => type.TypeArgument(0, value => value.Nullable()));

entity.HasQuery("GetBoxes").ProjectTo("ProjectToBox").List();
```

Queries retaining that final projection shape inherit the annotations. If a query changes shape
with `Select`, grouping, a scalar or dictionary terminal, configure that final value separately.
An explicit query declaration takes precedence. Changing the final CLR type after configuring
result nullability is rejected rather than guessed. Legacy projection helpers also honor the
projection declaration; closed consumers still see only their configured named operations.

Generated annotations also apply to matching projection constructors and casts. Final pipeline
annotation casts have the same CLR row type; they do not add a client-side projection or SQL
materialization step. Business null guarantees and provider translation remain designer-owned.

## Application ports and API baselines

Bound application interfaces are checked recursively, including array elements, generic children,
nullable structs and nested types. Binding remains exact by default: a nullable interface is not
silently used to change a designer declaration.
Mixed jagged/rectangular ranks retain their CLR shape too: C#'s consecutive rank specifiers are
outermost-first, while a nullable inner array introduces a separate type node. Rendering is
verified by compiler/reflection round trips, not merely matching a display string.

Overload identity is tracked separately using the CLR input types. `string[]` and `string?[]`
cannot become two overloads. `int[]` and `int?[]` **are** different CLR input types.
Optional [API snapshots](../generation/API-COMPATIBILITY.md) preserve annotations and report changed ones as
`RGA1004`, while retaining the unchanged CLR operation signature.

| Diagnostic | Meaning |
| --- | --- |
| RGN0001 | Duplicate input annotation declarations; combine them into one tree |
| RGN0002 | Unknown input name or mismatched input CLR type |
| RGN0003 | Invalid array/generic path or a reference-nullability annotation on a value type |
| RGN0004 | Result CLR type mismatch, or command result annotations without `Returns` |
| RGN0005 | Duplicate result/projection annotation declarations |
| RGN0006 | Key annotations have no dictionary/lookup terminal, mismatch its key CLR type, or make the outer key nullable |

## Independent dictionary/lookup keys

```csharp
entity.HasQuery("Buckets").Select(row => row)
    .Lookup(row => new BucketKey<string?>(row.Category), row => row.Description)
    .KeyNullability<BucketKey<string>>(key => key.TypeArgument(0, item => item.Nullable()))
    .ResultNullability<string>(value => value.Nullable());
```

This returns `IReadOnlyDictionary<BucketKey<string?>, IReadOnlyList<string?>>`. The key instance
remains non-null, while its component and the lookup values may be null. `KeyNullability` is
available on both query-builder forms; it does not filter null keys, change duplicate-key handling,
or change a CLR overload identity. The same trees participate in exact port binding and API
compatibility. Accessor inputs can also declare `InputNullability<T>(...)` after selecting an
optional [type adapter](TYPE-ADAPTERS.md).

The [Library designer](../../../samples/ConfigurationExamples/Library/Configuration/LibraryRepositoryDesigner.cs)
and [consumer](../../../samples/ConfigurationExamples/Library/Program.cs) execute the first example,
including null/empty lists, nullable elements, a missing author, and owned no-tracking read leases.
