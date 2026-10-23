# Optional type adapters: IDs and application models

Keep primitive storage keys inside persistence while exposing application-owned IDs and DTOs.
Configure the mapping once in the designer and reuse it in accessor inputs, queries and commands.
Nothing changes when adapters are absent: no mapper service, runtime adapter dependency, global
type convention or extra constructor parameter is introduced.

## A strongly typed ID

```csharp
var taskId = modelBuilder.HasTypeAdapter<int, TaskId>(
    value => new TaskId(value),       // model -> application contract
    value => value.Value);           // application contract -> model

modelBuilder.Entity<DeskTask>(entity =>
{
    entity.HasAccessor(task => task.Id).UseAdapter(taskId);

    entity.HasQuery("Find")
        .Where<TaskId>((task, id) => task.Id == taskId.ToModel(id))
        .Select(task => taskId.ToContract(task.Id))
        .SingleOrDefault();
});
```

On a selector surface, `ById(TaskId id)` replaces the primitive input for this accessor. The same
property mapping applies to every inferred primary-key/index accessor containing that property,
including composite keys/indexes. The other columns retain their original input types. A mapped
explicit accessor retains inferred single-column key/unique-index cardinality unless `IsUnique(...)`
deliberately overrides it. Composite uniqueness is still taken from its EF key/index.

Mappings are selected **per property**, not globally by CLR type. Mapping an `int` ID cannot silently
turn every `int` quantity or priority into that ID. Use `HasMethodName(...)` to rename its explicit
accessor, `Exclude(...)` to suppress a lookup property, or `ClearInputMapping()` to restore its
native input type. Clearing a mapping also clears its input annotation declaration; method names,
explicit uniqueness choices and the adapted accessor's inferred-cardinality policy are retained.

Closed mode still exposes only configured named operations, not an automatic CRUD port. Its
internal inferred accessors can be adapted, but declare `Where<TaskId>(...)` or a typed request
for the application-facing operation. Existing primitive overloads are not silently converted.

## Reusable request/DTO mappings

```csharp
var summary = modelBuilder.HasTypeAdapter<DeskTask, TaskSummary>(
    task => new TaskSummary(taskId.ToContract(task.Id), task.Title),
    value => new DeskTask { Id = taskId.ToModel(value.Id), Title = value.Title });

var create = modelBuilder.HasTypeAdapter<DeskTask, CreateTask>(
    task => new CreateTask(task.Title),
    request => new DeskTask { Title = request.Title });

modelBuilder.Entity<DeskTask>(entity =>
{
    entity.HasProjection(summary); // named ProjectToTaskSummary
    entity.HasQuery("ListTasks").OrderBy(task => task.Id)
        .ProjectTo<TaskSummary>().List();

    entity.HasCommand("Create")
        .Create<CreateTask>(request => create.ToModel(request))
        .Save()
        .Returns(task => summary.ToContract(task));
});
```

Adapters can compose other registered adapters. They are explicit mappings, not automatically
inferred inverses: a DTO's read mapping need not initialize all fields when mapped back. A create
operation therefore normally has a separate request mapping that omits generated IDs.

`ToModel(...)` / `ToContract(...)` are **design-time markers** inside expression trees. The builder
substitutes their expression bodies before loading/dependency analysis, write validation, source
rendering and contract generation. Generated code uses ordinary constructors/member access;
neither services nor the contracts project reference `RepositoryTypeAdapter`. `HasProjection(adapter)`
is a shortcut for its entity-to-contract expression; existing named/type projection reuse applies.

Markers also work in filters/fragments, visibility policies, `From` factories, result stages,
grouping, sorting, cursor selection, dictionary keys/values, command setters/results and request
guards/context bindings. Navigation reads inside a mapped projection remain visible to automatic
loading and dependency inventory. Mapping does not bypass protected columns, scalar-create/patch
rules, concurrency checks or the EF-free contract boundary.

Self-contained expression and provider rules still apply. Capturing an adapter local/field is
allowed (instance fields, not static initialization); ordinary captured configuration values remain rejected. Adapter properties/factories
are not invoked to discover a mapping. All adapters used by an operation must belong to that
builder (including modules applied to it); recursive mapping definitions are rejected. Calling a
marker outside an expression throws `RGD1701`.

The generator does not make arbitrary CLR methods SQL-translatable. Input conversion normally
evaluates on the parameter, leaving the mapped database column unchanged. Final DTO construction
may occur during materialization, as with a handwritten projection; filters/sorts must remain
provider-translatable. This is not authorization, graph reconciliation or a serialization framework.

## Reuse an existing EF value converter

If an entity key already uses a strongly typed CLR value and an EF converter, its inferred input
already has that model CLR type. Opt into the provider-facing input without repeating the reverse
conversion:

```csharp
// EF configuration:
modelBuilder.Entity<DirectoryEntry>().Property(entry => entry.Id)
    .HasConversion(value => value.Value, value => new StorageDirectoryId(value));

// Repository designer: ById(Guid id), including Guid inputs in composite indexes.
entity.HasAccessor(entry => entry.Id).UseValueConverter<Guid>();
```

RepoGen reads the finalized property's EF converter and emits its `ConvertFromProviderExpression`.
It does not change EF configuration, write values, column types or provider behavior. Exact
model/provider CLR types must match; absent/mismatched converters and unsupported/captured bodies
have `RGD1704`. For lifted nullable value types or a different application representation, declare
an explicit adapter instead. Nullable reference converters preserve EF's null-bypass behavior.

`UseAdapter(...)` and `UseValueConverter<TProvider>()` are mutually exclusive for a property;
call `ClearInputMapping()` before replacing a choice. Multiple adapters between the same CLR pair
are allowed because every use explicitly selects a token.

## Reference annotations and dictionary keys

Adapter expressions cannot retain generic reference annotations any more than ordinary expression
trees can. Use the existing input/result/projection annotation options. Accessor inputs support:

```csharp
entity.HasAccessor(row => row.Id).UseAdapter(keyAdapter)
    .InputNullability<CompositeId<string>>(type => type
        .TypeArgument(0, component => component.Nullable()));
```

`KeyNullability<TKey>(...)` configures dictionary/lookup keys independently from
`ResultNullability<TValue>(...)`, which still describes values:

```csharp
entity.HasQuery("ByBucket")
    .Select(task => task)
    .Lookup(task => new TaskBucket<string?>(task.ProjectId, task.Tag), task => task.Title)
    .KeyNullability<TaskBucket<string>>(type => type
        .TypeArgument(0, tag => tag.Nullable()));
```

The key object stays non-null but its `string` component may be nullable. The declaration is also
available on `HasQuery(...)` before entering the result builder. It must match the terminal key's
CLR type; nullable outer keys and annotations on a non-dictionary terminal are rejected as
`RGN0006`. No null-key filtering/default keys or duplicate-key policy is invented. Ordinary
dictionary duplicate/null keys still fail during materialization; lookup groups duplicate non-null
keys. Port binding and API snapshots retain independent key/value annotations.

Accessor/filter overloads also use their actual CLR input identities. Different reference
annotations cannot create a second overload (`RGD1705`); nullable value types remain distinct.

## Examples and validation

[TaskDesk designer](../../../samples/ConfigurationExamples/Adapters/Configuration/TaskDeskRepositoryDesigner.cs) drives
two independent examples:

- [Adapters](../../../samples/ConfigurationExamples/Adapters/Program.cs): EF-free handwritten ports,
  typed task/project IDs, reusable create/DTO mappings, saved generated IDs and nullable-part lookup keys.
- [AccessorAdapters](../../../samples/ConfigurationExamples/AccessorAdapters/Program.cs): read-only CQRS
  selectors with a converter-backed GUID key, a selectively mapped code and composite-index inputs.

Both use the simple preset and borrowed contexts: no cache, tenancy, lease or mapper service.
Run them with the normal [configuration script](../../../samples/ConfigurationExamples/README.md).
