using RonSijm.RepoGen.AdventureWorks.Model;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.AdventureWorks.Generation;

public sealed class AdventureWorksRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseGeneratedNamespace("RonSijm.RepoGen.AdventureWorks.Repositories"));

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasProjection<ProductSummary>(product => new ProductSummary(
                product.ProductId, product.Name, product.ProductNumber, product.ListPrice,
                product.ProductSubcategory == null ? null : product.ProductSubcategory.Name));
            entity.HasQuery("SearchCatalog")
                .Where<string>((product, prefix) => product.Name.StartsWith(prefix))
                .OrderBy(product => product.Name)
                .AsReadOnly()
                .ProjectTo<ProductSummary>()
                .List();
        });

        modelBuilder.Entity<SalesOrderHeader>(entity =>
        {
            entity.HasProjection<OrderSummary>(order => new OrderSummary(
                order.SalesOrderId, order.CustomerId, order.OrderDate, order.TotalDue, order.SalesOrderDetails.Count));
            entity.HasQuery("FindOrdersSince")
                .Where<DateTime>((order, since) => order.OrderDate >= since)
                .OrderByDescending(order => order.OrderDate)
                .AsReadOnly()
                .ProjectTo<OrderSummary>()
                .List();
        });
    }
}
