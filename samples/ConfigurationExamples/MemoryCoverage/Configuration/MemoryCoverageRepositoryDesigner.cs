// Designer -> ../Model/MemoryCoverageContext.cs -> ../Generated/TestDoubles.g.cs and coverage manifest.
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCoverage;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class MemoryCoverageRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryCoverage")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryCoverage")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryCoverage")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<ReviewedEntry>(entity =>
        {
            entity.HasInMemoryStore(row => row.Id, row => new ReviewedEntry
                { Id = row.Id, Name = row.Name, Hidden = row.Hidden, ReviewerId = row.ReviewerId });
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);

            // Deliberately use the inspectable RepoGen policy, not an implicitly dropped EF filter.
            entity.HasQuery("PortableList").IgnoreQueryFilters().OrderBy(row => row.Id)
                .Select(row => new ReviewedItem(row.Id, row.Name)).List();
            entity.HasQuery("EfFilteredCount").RequireTestOverride("EF model filters are verified against the real provider.").Count();
            entity.HasQuery("WithReviewer").IgnoreQueryFilters()
                .Select(row => new ReviewedItemWithReviewer(row.Id, row.Name, row.Reviewer == null ? "" : row.Reviewer.Name))
                .RequireTestOverride("Relational navigation loading belongs in provider tests; script the response in service tests.").List();
            entity.HasCommand("Delete").Where<int>((row, id) => row.Id == id).Delete();
        });
    }
}
