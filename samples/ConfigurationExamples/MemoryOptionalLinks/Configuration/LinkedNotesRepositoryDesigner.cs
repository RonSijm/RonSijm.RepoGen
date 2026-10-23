using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryOptionalLinks;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryOptionalLinks;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Nullable target links with explicit non-null query semantics; nullable source links on the inverse.</summary>
public sealed class LinkedNotesRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryOptionalLinks")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Generated.MemoryOptionalLinks")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryOptionalLinks")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        modelBuilder.Entity<LinkedNote>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<LinkedNoteSeed, (int, int)>(row => new ValueTuple<int, int>(row.TenantId, row.Id), row => new(row.TenantId, row.Id, row.Title))
                .MapInMemoryEntity<LinkedNoteSeed>(row => new LinkedNote { TenantId = row.TenantId, Id = row.Id, Title = row.Title });
            entity.HasInMemoryNavigation(row => row.Labels);
            entity.HasQuery("Cards").OrderBy(row => row.TenantId).ThenBy(row => row.Id)
                .Select(row => new NoteLabels(row.Title, row.Labels.Where(label => label != null)
                    .OrderBy(label => label.Id).Select(label => label.Name).ToList())).List();
            entity.HasQuery("PublicCards").OrderBy(row => row.TenantId).ThenBy(row => row.Id)
                .Select(row => new NoteLabels(row.Title, row.Labels.Where(label => label != null && !label.Hidden)
                    .OrderBy(label => label.Id).Select(label => label.Name).ToList())).List();
            entity.HasQuery("Counts").OrderBy(row => row.TenantId).ThenBy(row => row.Id)
                .Select(row => row.Labels.Where(label => label != null).Count()).List();
            entity.HasQuery("RawCounts").OrderBy(row => row.TenantId).ThenBy(row => row.Id).Select(row => row.Labels.Count)
                .RequireTestOverride("Unguarded SQL counts may retain null targets; use an explicit script or relational fixture.").List();
        });
        modelBuilder.Entity<NoteLabel>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<LabelSeed, int>(row => row.Id, row => new(row.Id, row.Name, row.Hidden))
                .MapInMemoryEntity<LabelSeed>(row => new NoteLabel { Id = row.Id, Name = row.Name, Hidden = row.Hidden });
            entity.HasInMemoryNavigation(row => row.Notes);
            entity.HasVisibilityPolicy("Public", row => !row.Hidden);
            // LabelId is optional on this edge's source, but NoteId is required on its target.
            // Null source keys cannot belong to a label: the ordinary count needs no target guard.
            entity.HasQuery("Usage").OrderBy(row => row.Id).Select(row => new LabelUsage(row.Name, row.Notes.Count)).List();
        });
        modelBuilder.Entity<NoteLabelLink>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<LabelLinkSeed, int>(row => row.Id, row => new(row.Id, row.TenantId, row.NoteId, row.LabelId))
                .MapInMemoryEntity<LabelLinkSeed>(row => new NoteLabelLink { Id = row.Id, TenantId = row.TenantId, NoteId = row.NoteId, LabelId = row.LabelId });
        });
    }
}
