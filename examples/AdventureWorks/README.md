# AdventureWorks

A public RepoGen example covering Microsoft's full AdventureWorks OLTP table model:

- 71 tables and repositories, 486 columns, 71 primary keys, and 90 foreign keys.
- 93 source indexes, including unique and composite lookups.
- Ten computed columns, SQL Server identity values, `hierarchyid`, and `geography`.
- Catalog searches and order summaries configured once in a `RepositoryDesigner`.

The source is the [Microsoft SQL Server samples repository](https://github.com/microsoft/sql-server-samples/tree/master/samples/databases/adventure-works),
tracked as the `sql-server-samples` Git submodule. The initial pin is
`e5d7bb90ca0b695ac14f61458047bfb5a498459e`. Microsoft's [MIT notice](THIRD-PARTY-NOTICES.md)
applies to its sample source and the model derived from it; retain that notice when redistributing.

## Run the checked-in demo

The generated model and repositories are checked in. With the repository's .NET SDK installed:

```powershell
dotnet run --project examples/AdventureWorks/Demo
```

The default run constructs every repository, validates model coverage, translates unique/composite/
hierarchy key lookups and a joined DTO projection into SQL, and checks that `Add` stages an entity.
It prints SQL and opens no database connection. The generated application targets .NET 8; the two
independent generation tools target .NET 10.

## Regenerate or check

Initialize the source submodule when cloning without `--recurse-submodules`:

```powershell
git submodule update --init --depth 1 -- examples/AdventureWorks/sql-server-samples
./examples/AdventureWorks/Generate.ps1 -Run
./examples/AdventureWorks/Generate.ps1 -Check -Run
```

`-Check` reports stale model/repository files and returns a failing exit code without rewriting them.
Generation does not install SQL Server, execute the install script, or import any data.

Only four upstream files are needed. This checkout uses sparse checkout to avoid materializing the
rest of Microsoft's large repository; an ordinary initialized submodule also works. To narrow an
existing checkout:

```powershell
git -C examples/AdventureWorks/sql-server-samples sparse-checkout set --no-cone /license.txt /README.md /samples/databases/adventure-works/README.md /samples/databases/adventure-works/oltp-install-script/instawdb.sql
```

## Try a restored database

Restore Microsoft's [AdventureWorks OLTP backup](https://github.com/microsoft/sql-server-samples/releases/tag/adventureworks)
to your SQL Server instance, then supply your connection string locally:

```powershell
$env:ADVENTUREWORKS_CONNECTION_STRING = 'Server=localhost;Database=AdventureWorks;Integrated Security=true;TrustServerCertificate=true'
dotnet run --project examples/AdventureWorks/Demo -- --database
```

This also reads catalog search results and recent order summaries. It does not save changes.
The date range follows the newest order in that database, accommodating different sample releases.

The application calls generated methods:

```csharp
IProductRepository products = new ProductRepository(context);
var matches = await products.SearchCatalogAsync("Road");
var product = await products.ByProductNumber("BK-M68B-42").AsEntityAsync();

ISalesOrderHeaderRepository orders = new SalesOrderHeaderRepository(context);
var summaries = await orders.FindOrdersSinceAsync(since);
```

## Layout

| Path | Purpose |
| --- | --- |
| `sql-server-samples/` | Unmodified, pinned upstream Git submodule. |
| `Scaffolding/` | Reads table DDL with Microsoft's ScriptDom parser and uses EF Core's C# scaffolder. |
| `Model/Generated/` | All 71 entity classes and the `AdventureWorksContext`. |
| `Model/schema.json` | Source checksum (UTF-8, LF-normalized) and schema coverage counts. |
| `Model/QueryResults.cs` | Application-owned catalog/order DTOs. |
| `Generation/AdventureWorksRepositoryDesigner.cs` | Default repositories plus two named queries/projections. |
| `Demo/Generated/` | RepoGen output, split by entity, with a generation manifest. |
| `Demo/Program.cs` | Executable checks and optional SQL Server reads. |

The scaffolder preserves explicit junction entities, including `SalesOrderHeaderSalesReason`, so
every table gets its own repository. It maps SQL alias types to their underlying types and supplies
the ten computed expressions' known result types, which SQL Server would normally report through
catalog metadata. It checks table, column, and foreign-key coverage before writing the model.

This is a table/repository example, not a database deployment tool: views, stored procedures,
triggers, XML schema collections, CHECK constraints, and full-text indexes remain in the upstream
install script. Updating the submodule requires reviewing the schema and rerunning generation;
the scaffolder deliberately rejects unexpected table/key coverage or unknown computed columns.
