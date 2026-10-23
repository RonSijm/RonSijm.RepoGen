using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.AdventureWorks.Model;
using RonSijm.RepoGen.AdventureWorks.Repositories;

namespace RonSijm.RepoGen.AdventureWorks.Demo;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 1 || args.Length == 1 && args[0] != "--database")
        {
            Console.Error.WriteLine("Usage: AdventureWorks.Demo [--database]");
            return 2;
        }

        var connection = Environment.GetEnvironmentVariable("ADVENTUREWORKS_CONNECTION_STRING");
        if (args.Length == 1 && string.IsNullOrWhiteSpace(connection))
        {
            Console.Error.WriteLine("Set ADVENTUREWORKS_CONNECTION_STRING to a restored AdventureWorks database before using --database.");
            return 2;
        }

        await using var context = AdventureWorksDatabase.CreateContext(connection);
        VerifyModel(context);
        VerifyRepositories(context);
        VerifySql(context);
        VerifyStagedWrites(context);
        Console.WriteLine("Verified 71 repositories: complete model, unique/composite/hierarchy lookups, projection SQL, and staged writes. No database connection opened.");

        if (args.Length == 1)
        {
            IProductRepository products = new ProductRepository(context);
            var results = await products.SearchCatalogAsync("Road");
            foreach (var product in results.Take(10))
            {
                Console.WriteLine($"{product.ProductNumber}: {product.Name} ({product.Subcategory}), {product.ListPrice:0.00}");
            }

            ISalesOrderHeaderRepository orders = new SalesOrderHeaderRepository(context);
            // Anchor to the database's dates so this works with older and newer AdventureWorks releases.
            var latest = await context.SalesOrderHeaders.MaxAsync(order => order.OrderDate);
            var recent = await orders.FindOrdersSinceAsync(latest.AddDays(-7));
            foreach (var order in recent.Take(10))
            {
                Console.WriteLine($"Order {order.SalesOrderId}: {order.LineCount} lines, total {order.TotalDue:0.00}");
            }
        }

        return 0;
    }

    private static void VerifyModel(AdventureWorksContext context)
    {
        var model = context.GetService<IDesignTimeModel>().Model;
        Require(model.GetEntityTypes().Count() == 71, "Expected all 71 table entities.");
        Require(model.GetEntityTypes().Sum(entity => entity.GetProperties().Count()) == 486, "Expected all 486 columns.");
        Require(model.GetEntityTypes().Sum(entity => entity.GetForeignKeys().Count()) == 90, "Expected all 90 relationships.");
        Require(model.GetEntityTypes().Sum(entity => entity.GetProperties().Count(property => property.GetComputedColumnSql() is not null)) == 10,
            "Expected all ten computed columns.");
        Require(model.FindEntityType(typeof(SalesOrderDetail))!.FindPrimaryKey()!.Properties.Count == 2, "Composite primary key was lost.");
        Require(model.FindEntityType(typeof(Document))!.FindProperty(nameof(Document.DocumentNode))!.ClrType == typeof(HierarchyId),
            "The hierarchyid key must use the SQL Server hierarchy provider.");
        Require(model.FindEntityType(typeof(Address))!.FindProperty(nameof(Address.SpatialLocation))!.GetColumnType() == "geography",
            "The geography column must retain its SQL Server mapping.");
        Require(model.FindEntityType(typeof(TransactionHistory))!.FindProperty(nameof(TransactionHistory.TransactionId))!.GetIdentitySeed() == 100000,
            "The non-default SQL Server identity seed was lost.");
    }

    private static void VerifyRepositories(AdventureWorksContext context)
    {
        var repositories = typeof(ProductRepository).Assembly.GetTypes().Where(type => type.IsClass && !type.IsAbstract &&
            type.Namespace == typeof(ProductRepository).Namespace && type.Name.EndsWith("Repository", StringComparison.Ordinal)).ToArray();
        Require(repositories.Length == 71, "Expected a concrete generated repository for every table.");
        foreach (var repository in repositories)
        {
            Require(Activator.CreateInstance(repository, context) is not null, "Could not construct " + repository.Name);
        }
    }

    private static void VerifySql(AdventureWorksContext context)
    {
        var products = new ProductRepository(context);
        // The selectors' internal Query is accessible here because the generated code compiles in this demo.
        // This inspects SQL without sending it to SQL Server; normal consumers use the public terminal methods.
        var productSql = products.ByProductNumber("BK-M68B-42").Query.ToQueryString();
        Require(productSql.Contains("[Production].[Product]", StringComparison.Ordinal) &&
            productSql.Contains("[ProductNumber] =", StringComparison.Ordinal), "The unique index lookup did not translate.");

        var details = new SalesOrderDetailRepository(context);
        var compositeSql = details.BySalesOrderIdAndSalesOrderDetailId(43659, 1).Query.ToQueryString();
        Require(compositeSql.Contains("[SalesOrderID] =", StringComparison.Ordinal) &&
            compositeSql.Contains("[SalesOrderDetailID] =", StringComparison.Ordinal), "The composite key lookup did not translate both columns.");

        var documents = new DocumentRepository(context);
        var documentSql = documents.ByDocumentNode(HierarchyId.Parse("/1/")).Query.ToQueryString();
        Require(documentSql.Contains("[DocumentNode] =", StringComparison.Ordinal), "The hierarchyid lookup did not translate.");

        var projectionSql = products.GetAll().Query.Select(product => new ProductSummary(
            product.ProductId, product.Name, product.ProductNumber, product.ListPrice,
            product.ProductSubcategory == null ? null : product.ProductSubcategory.Name)).ToQueryString();
        Require(projectionSql.Contains("LEFT JOIN", StringComparison.Ordinal) &&
            !projectionSql.Contains("[StandardCost]", StringComparison.Ordinal), "The catalog projection must join the subcategory and select only DTO columns.");
        Console.WriteLine(productSql);
        Console.WriteLine(compositeSql);
        Console.WriteLine(documentSql);
        Console.WriteLine(projectionSql);
    }

    private static void VerifyStagedWrites(AdventureWorksContext context)
    {
        var product = new Product { Name = "RepoGen staged write", ProductNumber = "DEMO-001" };
        var repository = new ProductRepository(context);
        repository.Add(product);
        Require(context.Entry(product).State == EntityState.Added, "The generated Add method did not stage the entity.");
        context.ChangeTracker.Clear();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
