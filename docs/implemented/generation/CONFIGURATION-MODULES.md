# Repository configuration modules

Keep global generation options in the `RepositoryDesigner` and place entity operations in classes implementing `IRepositoryEntityConfiguration<TEntity>`:

```csharp
internal sealed class BookConfiguration : IRepositoryEntityConfiguration<Book>
{
    public bool? WriteEnabled => false;
    public bool? ProjectionEnabled => false;

    public RepositoryResultQueryBuilder<BookSummary> FindByTitle(RepositoryEntityBuilder<Book> entity)
    {
        return entity.HasQuery(nameof(FindByTitle))
            .Where<string>((row, prefix) => row.Title.StartsWith(prefix))
            .OrderBy(row => row.Id)
            .Select(row => new BookSummary(row.Id, row.Title)).List();
    }
}
```

Register instances directly when configuration needs constructor arguments or deliberate ordering:

```csharp
modelBuilder.ApplyConfiguration(new BookConfiguration());
```

For a large set of configurations, select their assembly and optionally filter candidate types:

```csharp
protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
{
    modelBuilder.Configure(options => options.UseSimpleDefaults()
        .UseClosedRepositories("My.Application.Contracts")
        .GenerateOnlyConfiguredEntities());

    modelBuilder.ApplyConfigurationsFromAssembly(
        typeof(BookConfiguration).Assembly,
        type => type.Namespace == typeof(BookConfiguration).Namespace);
}
```

Discovery supports `IRepositoryModelConfiguration` for modules that configure multiple entities, shared fragments, adapters or options. Existing fluent configuration and generation validation are unchanged. No assembly is scanned implicitly and generated repositories do not perform discovery.

## Entity defaults and optional configuration

`IRepositoryEntityConfiguration<TEntity>` provides nullable `RepositoryEnabled`, `ReadEnabled`, `WriteEnabled` and `ProjectionEnabled` properties, each defaulting to `null`. Override only the settings the module needs. A non-null value sets the matching entity-builder flag; `null` preserves its current value, including settings applied by earlier modules. New entity flags normally start as `true`.

`Configure` has an empty default implementation. Omit it when properties and operation methods express the whole configuration. Implement it for shared projections, fragments, loading profiles or other custom setup. Both registration paths apply non-null properties first, call `Configure` second, and invoke operation methods last. Assignments inside `Configure` can override the property defaults. A class implementing multiple entity configuration contracts can use explicit interface properties to give each entity different settings.

The Design package targets .NET Standard 2.1 to support these default interface implementations. Its .NET 8 and .NET 10 consumers support this target.

## Operation methods

Both `ApplyConfiguration(instance)` and assembly discovery apply entity property defaults, run the optional `Configure`, then invoke public instance methods returning a repository operation builder. Each operation method must be non-generic and take exactly one `RepositoryEntityBuilder<TEntity>` for an entity configured by the class. Supported builders implement `IRepositoryOperationBuilder`: `RepositoryQueryBuilder<TEntity>`, `RepositoryResultQueryBuilder<TResult>` and `RepositoryCommandBuilder<TEntity>`. Returning the marker interface itself also works.

```csharp
public RepositoryCommandBuilder<Book> Delete(RepositoryEntityBuilder<Book> entity)
{
    return entity.HasCommand(nameof(Delete))
        .Where<int>((row, id) => row.Id == id)
        .TrackedDelete().Save();
}
```

`HasQuery` and `HasCommand` register operations immediately. Discovery invokes the method once per application; it does not register its return value again or supply a missing terminal. Finish the fluent definition with the appropriate query terminal or command persistence/result configuration. Existing operation validation still applies.

Declare entity flags as nullable properties and keep shared projections, loading profiles and fragments in the optional `Configure`. Store shared builder objects in instance fields when operation methods need them; all methods receive the same entity builder and configuration instance. Inline operations in `Configure` remain supported and run before discovered operations. Keep manually invoked helpers private so discovery does not call them again. Other property getters and methods with unrelated return types are not discovered as operations.

Inherited public operation methods are supported; virtual overrides run once. Invoke base-declared methods before derived-declared methods, then use metadata declaration order within each declaring type. This preserves operation ordering when extracting an existing single-file configuration. Relative ordering across partial-class files follows the compiler's metadata layout; use private helpers called explicitly from `Configure` for order-dependent setup. A class implementing multiple entity configuration interfaces invokes each operation only for its matching entity parameter.

Invalid signatures, null builder results and invocation failures report `RGD1007`, including the configuration type and method. Invocation failures retain the original exception. Static methods and generic methods returning operation builders are rejected; make ordinary helpers private. Discovery remains explicit through the configuration APIs, and it does not scan arbitrary methods on the root designer.

When using CA1822, scope an analyzer exception to configuration operation methods so it does not suggest making them static. Discovery requires instance methods.

## Discovery and ordering

- Scan only the supplied assembly. The predicate runs on eligible configuration types before construction, so it can select a namespace or application-owned marker interface.
- Skip abstract classes and open generic types. Discover inherited configuration contracts and non-public classes.
- Require a parameterless instance constructor, including a non-public one. Use direct registration for modules with constructor arguments, and exclude them from discovery with the predicate.
- Apply modules in ordinal full-type-name order. Construct a module once per scan. If it implements both model and entity contracts, apply its model configuration first, then its entity contracts in ordinal entity-type-name order. Explicit interface implementations work.
- Repeated calls apply modules again. Multiple modules can contribute to one entity; later scalar settings override earlier values, while operations/projections accumulate. Existing duplicate-operation validation remains active. Use explicit registrations or separate filtered scan calls when configuration has a deliberate precedence relationship.
- Report `RGD1006` for assembly type-loading failures, missing constructors and construction/configuration failures. Preserve the original exception. Type-load failures do not silently apply an incomplete assembly.

Reflection is confined to design execution. Discovery does not require DI or a new runtime dependency.

## Public examples

The [configuration examples](../../../samples/ConfigurationExamples/README.md) contain standalone models, designers, generated output and executable consumers. The [Library designer](../../../samples/ConfigurationExamples/Library/Configuration/LibraryRepositoryDesigner.cs) demonstrates loading profiles, projections and named queries for a book catalog.

For a larger catalog, move each entity's declarations into a configuration class like `BookConfiguration` above and register the classes through a filtered assembly scan. Keep context-wide options on the root designer and application-specific mappings in the consumer project.
