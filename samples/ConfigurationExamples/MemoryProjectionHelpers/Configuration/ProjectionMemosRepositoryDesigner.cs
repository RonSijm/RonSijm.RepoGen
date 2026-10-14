using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryProjectionHelpers;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryProjectionHelpers;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Original DTO helper in production, explicit flat DTO expression for independently opted-in fixtures.</summary>
public sealed class ProjectionMemosRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryProjectionHelpers")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryProjectionHelpers")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryProjectionHelpers")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.HasInMemoryProjectionTranslation<int, string, int, MemoCard>("MemoCard",
            (id, text, revision) => MemoCards.Create(id, text, revision),
            (id, text, revision) => new MemoCard(id, text.Trim().ToUpperInvariant(), revision, text.Length));
        modelBuilder.Entity<StoredProjectionMemo>(entity =>
        {
            entity.BindReadPort<IProjectionMemoReader>().BindWritePort<IProjectionMemoWriter>();
            entity.HasInMemoryStore<ProjectionMemoSeed, int>(row => row.Id, row => new(row.Id, row.Text, row.Revision, row.Hidden))
                .MapInMemoryEntity<ProjectionMemoSeed>(row => new StoredProjectionMemo { Id = row.Id, Text = row.Text, Revision = row.Revision, Hidden = row.Hidden })
                .MapInMemoryWrites<ProjectionMemoSeed>((row, saved) => new(row.Id, row.Text, row.Revision, row.Hidden));
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasProjection<MemoCard>(row => MemoCards.Create(row.Id, row.Text, row.Revision));
            entity.HasQuery("Cards").OrderBy(row => row.Id).ProjectTo<MemoCard>().List();
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id).ProjectTo<MemoCard>().SingleOrDefault();
            entity.HasQuery("Page").OrderBy(row => row.Id).ProjectTo<MemoCard>().Paged();
            entity.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<MemoCard>().Stream();
            entity.HasCommand("Rename").Where<RenameMemo>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, RenameMemo>(row => row.Text, (row, request) => request.Text))
                .ExpectVersion<int, RenameMemo>(row => row.Revision, (row, request) => request.ExpectedRevision)
                .UseInMemoryTranslations().Save().ReturnsSaved(row => MemoCards.Create(row.Id, row.Text, row.Revision));
        });
    }
}
