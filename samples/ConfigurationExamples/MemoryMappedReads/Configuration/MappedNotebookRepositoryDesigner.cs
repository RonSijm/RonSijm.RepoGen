using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedReads;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedReads;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>One explicit scalar read API for a backing field, field-only values and a mapped indexer.</summary>
public sealed class MappedNotebookRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryMappedReads")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryMappedReads")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryMappedReads")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        modelBuilder.Entity<StoredMappedMemo>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<IMappedMemoReader>();
            entity.HasInMemoryStore<MappedMemoSeed, int>(row => row.Id,
                    row => new(row.Id, row.Caption, row.Revision, row.Source, row.Deleted))
                .MapInMemoryEntity<MappedMemoSeed>(row => new StoredMappedMemo { Id = row.Id })
                .MapInMemoryProperty<MappedMemoSeed, string>(nameof(StoredMappedMemo.Caption), row => row.Caption)
                .MapInMemoryProperty<MappedMemoSeed, int>("revision", row => row.Revision)
                .MapInMemoryProperty<MappedMemoSeed, bool>("deleted", row => row.Deleted)
                .MapInMemoryProperty<MappedMemoSeed, string>("Source", row => row.Source);
            entity.UseInMemoryModelQueryFilter(); // Separate permission to apply the context's field-only Deleted filter.
            entity.HasDefaultSort(row => row.Caption);
            entity.HasDefaultSort(row => row.Id);
            entity.HasProjection<MappedMemoCard>(row => new MappedMemoCard(row.Id, row.Caption,
                EF.Property<int>(row, "revision"), (string)row["Source"]!));
            entity.HasQuery("Cards").ProjectTo<MappedMemoCard>().List();
            entity.HasQuery("Get").Where<int>((row, id) => row.Id == id).ProjectTo<MappedMemoCard>().SingleOrDefault();
            entity.HasQuery("Sorted").AllowSorting("field", "descending", row => row.Caption).ProjectTo<MappedMemoCard>().List();
            entity.HasQuery("Recent").Where<int>((row, minimumRevision) => EF.Property<int>(row, "revision") >= minimumRevision)
                .ProjectTo<MappedMemoCard>().List();
            entity.HasQuery("Page").ProjectTo<MappedMemoCard>().Paged();
        });
    }
}
