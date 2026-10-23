// Paired with ../Model/VisibilityContext.cs by ../../GenerationRunner/Program.cs.
// API: ../../Contracts/Generated/VisibilityRepositories.g.cs; implementation: ../Generated/Repositories.g.cs.
// See ../README.md for the declaration-to-output map; the consumer does not run this designer.
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.Visibility;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class VisibilityRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.Visibility")
            .RequireNavigationVisibilityReview());
        modelBuilder.Entity<ShowcaseOwner>(entity =>
        {
            entity.RepositoryEnabled = false;
            entity.HasVisibilityPolicy("ActiveOwners", owner => owner.Active);
        });
        modelBuilder.Entity<ShowcaseComment>(entity =>
        {
            entity.RepositoryEnabled = false;
            entity.HasVisibilityPolicy("PublicComments", comment => comment.Public);
        });
        modelBuilder.Entity<ShowcaseDocument>(entity =>
        {
            entity.WriteEnabled = false;
            entity.ProjectionEnabled = false;
            entity.BindReadPort<IReviewedLibrary>();
            entity.HasVisibilityPolicy("CurrentDocuments", document => document.Active);
            entity.HasProjection(document => new ReviewedDocumentSummary(document.Id, document.Title,
                    document.Owner == null ? null : document.Owner.Name))
                .ReviewNavigationVisibility<ShowcaseOwner>("ActiveOwners", "The EF global filter protects optional owner navigation on ordinary reads.");
            entity.HasLoadProfile("PublicDetail", load =>
            {
                load.Include(document => document.Owner);
                load.Include(document => document.Comments.Where(comment => comment.Public).OrderBy(comment => comment.Id));
            });
            entity.HasProjection(document => VisibilityMapping.Details(document)).AutoIncludeNavigations(false);

            entity.HasQuery("GetSummaries").OrderBy(document => document.Id).ProjectTo<ReviewedDocumentSummary>().List();
            entity.HasQuery("GetDetails").UseLoadProfile("PublicDetail").Where<int>((document, id) => document.Id == id)
                .ReviewNavigationVisibility<ShowcaseOwner>("ActiveOwners", "The EF global filter protects the loaded owner reference.")
                .ReviewNavigationVisibility<ShowcaseComment>("PublicComments", "The attached profile explicitly loads only public comments.")
                .AsSplitQuery().ProjectTo<ReviewedDocumentDetails>().SingleOrDefault();
            // Query receipts override inherited projection receipts; IgnoreQueryFilters is a reviewed design-time choice.
            entity.HasQuery("GetTrustedHistory").IgnoreQueryFilters()
                .ReviewNavigationVisibility<ShowcaseOwner>("ActiveOwners", "Trusted historical view deliberately includes inactive owners; current document policy remains active.")
                .OrderBy(document => document.Id).ProjectTo<ReviewedDocumentSummary>().List();
            // Typed roots already receive root policies, so an explicit reporting anti-join needs no navigation receipt.
            entity.HasQuery("GetWithoutOwners").From(sources => sources.Set<ShowcaseDocument>()
                .Where(document => !sources.Set<ShowcaseOwner>().Any(owner => (int?)owner.Id == document.OwnerId))
                .OrderBy(document => document.Id).Select(document => document.Id)).List();
        });
    }
}
