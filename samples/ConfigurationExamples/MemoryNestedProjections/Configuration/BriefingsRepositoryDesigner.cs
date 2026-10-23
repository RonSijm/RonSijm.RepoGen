using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNestedProjections;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNestedProjections;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Named application methods with nested result data, not service-side mapping or EF entity graphs.</summary>
public sealed class BriefingsRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryNestedProjections")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryNestedProjections")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryNestedProjections")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.HasInMemoryProjectionTranslation<int, string, int, BriefingCard>("BriefingCard",
            (id, title, revision) => BriefingCards.Create(id, title, revision),
            (id, title, revision) => new BriefingCard(id,
                new BriefingHeading(title.Trim().ToUpperInvariant(), revision),
                new List<BriefingFact> { new("length", title.Length), new("revision", revision) },
                title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => word.ToUpperInvariant()).ToArray()),
            allowNestedData: true);
        modelBuilder.Entity<StoredBriefing>(entity =>
        {
            entity.BindReadPort<IBriefingReader>().BindWritePort<IBriefingWriter>();
            entity.HasInMemoryStore<BriefingSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Revision, row.Hidden))
                .MapInMemoryEntity<BriefingSeed>(row => new StoredBriefing { Id = row.Id, Title = row.Title, Revision = row.Revision, Hidden = row.Hidden })
                .MapInMemoryWrites<BriefingSeed>((row, saved) => new(row.Id, row.Title, row.Revision, row.Hidden));
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasProjection<BriefingCard>(row => BriefingCards.Create(row.Id, row.Title, row.Revision));
            entity.HasQuery("Cards").OrderBy(row => row.Id).ProjectTo<BriefingCard>().List();
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id).ProjectTo<BriefingCard>().SingleOrDefault();
            entity.HasQuery("Page").OrderBy(row => row.Id).ProjectTo<BriefingCard>().Paged();
            entity.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<BriefingCard>().Stream();
            entity.HasCommand("Rename").Where<RenameBriefing>((row, input) => row.Id == input.Id)
                .Patch(setters => setters.SetProperty<string, RenameBriefing>(row => row.Title, (row, input) => input.Title))
                .ExpectVersion<int, RenameBriefing>(row => row.Revision, (row, input) => input.ExpectedRevision)
                .UseInMemoryTranslations().Save().ReturnsSaved(row => BriefingCards.Create(row.Id, row.Title, row.Revision));
        });
    }
}
