// Paired with ../Model/CatalogContext.cs by ../../GenerationRunner/Program.cs.
// Output: ../Generated/Repositories.g.cs (IProductRepository) and ../Generated/Queries.g.cs.
// See ../README.md for the declaration-to-output map; the consumer does not run this designer.
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.Catalog;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class CatalogRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        // Start small, then opt into batch/cache helpers and one neutrally named tracking control.
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .GenerateBatchOperations()
            .GenerateCacheDecorator()
            .GenerateTrackingMethods()
            .UseTrackingMethodNames(noTracking: "AsReadOnly", tracking: null, noTrackingWithIdentityResolution: null));

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasAccessor(product => product.Name);
            entity.Exclude(product => product.Category); // Suppress its automatic index lookup, not EF's property.
            entity.HasDefaultSort(product => product.Name);
            entity.HasSortableFields(product => product.Name, product => product.Amount);
            entity.HasProjection(product => new ProductSummary(product.Id, product.Name, product.Amount));
            entity.ProjectionEnabled = false; // Services must use the reviewed designer projection/queries.

            entity.HasQuery("GetProducts")
                .Where<string>((product, category) => product.Category == category)
                .AllowSorting(product => product.Name, product => product.Amount)
                .AsReadOnly()
                .ProjectTo<ProductSummary>()
                .Cache(cache => cache.For(TimeSpan.FromMinutes(5)))
                .Paged();
        });
    }
}
