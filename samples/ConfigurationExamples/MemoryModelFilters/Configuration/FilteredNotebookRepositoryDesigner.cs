using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelFilters;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryModelFilters;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Reuse final-model row filters in explicit fixtures, independently from designer visibility.</summary>
public sealed class FilteredNotebookRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryModelFilters")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryModelFilters")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryModelFilters")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        modelBuilder.Entity<StoredFilteredNote>(entity =>
        {
            entity.WriteEnabled = false; entity.BindReadPort<IFilteredNoteReader>();
            entity.HasInMemoryStore<FilteredNoteSeed, int>(row => row.Id, row => new(row.Id, row.Text, row.Deleted, row.Hidden, row.GroupId))
                .MapInMemoryEntity<FilteredNoteSeed>(row => new StoredFilteredNote { Id = row.Id, Text = row.Text, Deleted = row.Deleted, Hidden = row.Hidden, GroupId = row.GroupId });
            entity.UseInMemoryModelQueryFilter(); // Uses !Deleted from the context; do not repeat it here.
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasProjection<FilteredNoteCard>(row => new FilteredNoteCard(row.Id, row.Text));
            entity.HasQuery("Cards").OrderBy(row => row.Id).ProjectTo<FilteredNoteCard>().List();
            entity.HasQuery("IncludingDeleted").IgnoreQueryFilters().OrderBy(row => row.Id).ProjectTo<FilteredNoteCard>().List();
            entity.HasQuery("IncludingHidden").BypassVisibility("Visible").OrderBy(row => row.Id).ProjectTo<FilteredNoteCard>().List();
            entity.HasQuery("Page").OrderBy(row => row.Id).ProjectTo<FilteredNoteCard>().Paged();
            entity.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<FilteredNoteCard>().Stream();
            entity.HasQuery("ActiveGroups").From(sources => sources.Set<StoredFilteredNote>().Join(sources.Set<StoredFilteredGroup>(),
                note => note.GroupId, group => group.Id, (note, group) => new FilteredNoteCard(note.Id, note.Text))).OrderBy(card => card.Id).List();
        });
        modelBuilder.Entity<StoredFilteredGroup>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<FilteredGroupSeed, int>(row => row.Id, row => new(row.Id, row.Archived))
                .MapInMemoryEntity<FilteredGroupSeed>(row => new StoredFilteredGroup { Id = row.Id, Archived = row.Archived });
            entity.UseInMemoryModelQueryFilter(); // Each From root opts in separately.
        });
    }
}
