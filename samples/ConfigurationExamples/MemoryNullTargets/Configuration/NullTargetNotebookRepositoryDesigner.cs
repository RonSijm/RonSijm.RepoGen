using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNullTargets;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNullTargets;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Explicitly preserves absent many-to-many targets for null-safe reporting, not entity loading.</summary>
public sealed class NullTargetNotebookRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryNullTargets")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryNullTargets")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryNullTargets")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        modelBuilder.Entity<StoredNotebook>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<INotebookSummaryReader>();
            entity.HasInMemoryStore<NotebookSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Hidden))
                .MapInMemoryEntity<NotebookSeed>(row => new StoredNotebook { Id = row.Id, Title = row.Title, Hidden = row.Hidden });
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasInMemoryNavigation(row => row.Labels, preserveMissingTargets: true);
            entity.HasProjection<NotebookSummary>(row => new NotebookSummary(row.Id, row.Title, row.Labels.Count,
                row.Labels.Count(label => label == null), row.Labels.OrderBy(label => label == null ? 0 : label.Id)
                    .Select(label => label == null ? null : label.Name).ToList()));
            entity.HasQuery("Summaries").OrderBy(row => row.Id).ProjectTo<NotebookSummary>().List();
            entity.HasQuery("Page").OrderBy(row => row.Id).ProjectTo<NotebookSummary>().Paged();
            entity.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<NotebookSummary>().Stream();
            entity.HasQuery("HasMissing").Where(row => row.Labels.Any(label => label == null)).OrderBy(row => row.Id).Select(row => row.Id).List();
        });
        modelBuilder.Entity<StoredNotebookLabel>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<NotebookLabelSeed, int>(row => row.Id, row => new(row.Id, row.Name, row.Hidden))
                .MapInMemoryEntity<NotebookLabelSeed>(row => new StoredNotebookLabel { Id = row.Id, Name = row.Name, Hidden = row.Hidden });
        });
        modelBuilder.Entity<StoredNotebookLink>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<NotebookLinkSeed, int>(row => row.Id, row => new(row.Id, row.NotebookId, row.LabelId))
                .MapInMemoryEntity<NotebookLinkSeed>(row => new StoredNotebookLink { Id = row.Id, NotebookId = row.NotebookId, LabelId = row.LabelId });
        });
    }
}
