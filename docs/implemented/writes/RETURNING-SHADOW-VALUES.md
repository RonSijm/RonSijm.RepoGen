# Shadow values in native write results

`IncludeShadowProperties()` lets a configured native update/delete command return mapped scalar
values that have no CLR property. It is off by default, applies to one command, and does not add
EF methods or property names to the generated application contract.

EF normally keeps shadow values in its change tracker. A detached/no-tracking entity alone does
not carry that state. RepoGen therefore reads these values directly from the native returned
columns into a separate detached buffer, not from `Entry(entity)` or a second query.
See [EF shadow properties](https://learn.microsoft.com/en-us/ef/core/modeling/shadow-properties).

## Configure the result at design time

Select closed repositories and a native returning writer as in
[native update/delete-and-return](RETURNING-WRITES.md), then opt in on the relevant command:

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Contracts")
    .UseReturningWrites<SqliteReturningWriter>());

modelBuilder.Entity<Document>(entity =>
{
    entity.HasCommand("Rename")
        .Where<RenameDocument>((row, request) => row.Id == request.Id)
        .Update(setters => setters.SetProperty<string, RenameDocument>(
            row => row.Title, (row, request) => request.Title))
        .ReturnsMany(row => new DocumentChange(row.Id, row.Title,
            EF.Property<string>(row, "NormalizedTitle")))
        .IncludeShadowProperties();
});
```

The service still calls only `RenameAsync(request, cancellationToken)` and receives
`MutationResult<DocumentChange>`. `EF.Property` exists in the designer, not the public API or the
runtime result mapper. Request-bound `ReturnsMany<TResult, TRequest>` and explicit result
nullability work as for ordinary returning commands.

`IncludeShadowProperties(false)` removes the permission. It is not an entity-wide/global switch
and does not select a provider. Tracked/count-only commands cannot use it. Separately configured
[atomic insert/upsert capture](ATOMIC-SCALAR-CAPTURE.md) uses scalar Returns, atomic-writer capabilities
and its own transaction policy. Enabling a writer alone does not enable shadow mappings.

Field-backed/field-only/indexer result values have a separate, broader
[IncludeMappedProperties](RETURNING-MAPPED-VALUES.md) permission. The shadow-only option below
retains its narrower mapping and custom-writer capability requirements.

## Values and restrictions

- One ordinary scalar table remains required, with readable/writable CLR scalar members and
  ordinary CLR primary/composite keys. True shadow columns are allowed only under this option.
  Backing-field-only/indexer properties, shadow keys, inherited/shared/owned/multi-table roots,
  declared triggers and the other native writer restrictions remain unsupported.
- A returned `EF.Property<T>(row, "Name")` must use the current command row, a literal mapped
  scalar property name, and its exact model CLR type. Unknown/dynamic names, widened/boxed lookup
  types, other receivers, navigations and opaque whole-entity helpers fail with `RGD1306`.
  A named read of a normal CLR property is also supported.
- The result buffer contains all mapped scalar columns, including CLR values, nulls, binary
  values and values converted back to the model CLR type. This is not SQL-column pruning,
  recursive graph materialization, streaming or a guarantee of deeply immutable DTOs.
- Updates return direct post-update values; deletes return pre-delete values. Computed shadow
  fields and SQL Server shadow rowversions can be read. Undeclared trigger side effects and
  post-trigger rereads are not implied.
- Setter targets remain writable, non-generated, non-key CLR properties. This option does not
  permit assigning shadow/generated columns or automatically infer concurrency policies. Existing
  `ExpectVersion` supports [explicit typed named tokens](../repositories/MAPPED-PROPERTY-BINDINGS.md),
  including shadow properties; an explicit typed `Where` can also compare a shadow token where
  the provider translates it. Consumers still own the conflict policy.

## Transactions, tracking and cache

The same native executor handles ordinary and shadow-valued commands. It maps every result before
committing its own transaction or releasing the operation's savepoint. Projection/materialization
failure rolls back the operation; an enclosing transaction retains earlier successful operations
and still requires the caller's commit. Unsupported ambient/non-savepoint transactions remain
rejected before writing.

No entities are attached and `SaveChanges` is never called. Pending tracked CLR/shadow edits are
not saved or used as result values. Tracked copies remain stale; deliberate refresh/clear policies
belong in infrastructure. Shared unit-of-work observers and the optional cache retain their
commit-only semantics; failures and empty results do not publish successful changes.

## Custom writers and tests

The bundled SQLite and SQL Server writers implement `IRepositoryReturningStateWriter`, an
optional extension of `IRepositoryReturningWriter`. Existing ordinary writers need not change.
A command requesting shadow capture with a legacy writer fails at design time with `RGD1307`.
Implementing the extended interface means supplying `ReturningWriteRow<TEntity>` instances with
detached entities and exact-name, model-CLR scalar dictionaries from the native returned row.
The wrapper copies the dictionary; it never creates tracker entries.

This permission and `SimulateImmediateWrites()` alone do not authorize fixture shadow state.
Complete separately opted-in scalar write maps and explicit factories can simulate
[mapped returned values](../testing/INMEMORY-MAPPED-RETURNING.md), not provider audit/rowversion
behavior. Other shapes retain `RGD3402` in strict mode; use `RequireTestOverride(reason)` with a
typed result or a real-provider fixture. Unarranged unsupported operations never return false success.

The [ShadowReturning example](../../../samples/ConfigurationExamples/ShadowReturning/README.md)
maps its designer to all four generated artifacts and runs real SQLite assertions. Regression
tests cover conversions/nulls, quoted identifiers, filters, composite keys, computed fields,
tracking isolation, cancellation, rollback/savepoints, cache and strict fixture boundaries.
Additional live SQL Server cases require `REPOGEN_SQLSERVER_TEST_CONNECTION`; they own only
GUID-named fixture tables, never the supplied database.

[Writes](README.md) · [Generation options](../generation/GENERATION-OPTIONS.md)
