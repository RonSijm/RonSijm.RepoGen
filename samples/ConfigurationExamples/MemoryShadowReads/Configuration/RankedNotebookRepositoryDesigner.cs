using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryShadowReads;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryShadowReads;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Map exact model-only scalars to public fixture data without exposing EF or persistence types.</summary>
public sealed class RankedNotebookRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryShadowReads")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryShadowReads")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryShadowReads")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        modelBuilder.Entity<StoredRankedNote>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<IRankedNoteReader>();
            entity.HasInMemoryStore<RankedNoteSeed, int>(row => row.Id,
                    row => new(row.Id, row.Text, row.Hidden, row.Rank, row.Category, row.Deleted))
                .MapInMemoryEntity<RankedNoteSeed>(row => new StoredRankedNote { Id = row.Id, Text = row.Text, Hidden = row.Hidden })
                .MapInMemoryProperty<RankedNoteSeed, int>("Rank", row => row.Rank)
                .MapInMemoryProperty<RankedNoteSeed, string>("Category", row => row.Category)
                .MapInMemoryProperty<RankedNoteSeed, bool>("Deleted", row => row.Deleted);
            entity.UseInMemoryModelQueryFilter(); // Separate permission to reuse the context's !Deleted filter.
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasDefaultSort(row => EF.Property<int>(row, "Rank"));
            entity.HasDefaultSort(row => row.Id); // Deterministic tie-breaker.
            entity.HasProjection<RankedNoteCard>(row => new RankedNoteCard(row.Id, row.Text,
                EF.Property<int>(row, "Rank"), EF.Property<string>(row, "Category")));
            entity.HasQuery("Cards").Where<int>((row, minimumRank) => EF.Property<int>(row, "Rank") >= minimumRank)
                .ProjectTo<RankedNoteCard>().List();
            entity.HasQuery("IncludingDeleted").IgnoreQueryFilters().ProjectTo<RankedNoteCard>().List();
            entity.HasQuery("Page").ProjectTo<RankedNoteCard>().Paged();
            entity.HasQuery("Stream").ProjectTo<RankedNoteCard>().Stream();
            entity.HasQuery("Totals").GroupBy(row => EF.Property<string>(row, "Category"),
                    group => new RankedCategoryTotal { Category = group.Key, Count = group.Count(), RankTotal = group.Sum(row => EF.Property<int>(row, "Rank")) })
                .Results<RankedCategoryTotal>().OrderBy(row => row.Category).List();
        });
    }
}
