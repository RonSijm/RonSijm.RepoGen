// Paired with PrimaryNoteContext in ../Model/NoteStores.cs by ../../GenerationRunner/Program.cs.
// Output: ../Generated/RonSijm.RepoGen.ConfigurationExamples.MultiContext.Primary.StoredNote.Repository.g.cs (+ projections/infrastructure).
// See ../README.md for the declaration-to-output map; the consumer does not run this designer.
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.MultiContext;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class PrimaryNoteRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseCqrsSelectors()
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.MultiContext.Primary")
            .GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<StoredNote>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasProjection<StoredNoteSummary>(note => new StoredNoteSummary(note.Id, note.Text));
            entity.HasQuery("ListActive").Where(note => note.Active).OrderBy(note => note.Id)
                .ProjectTo<StoredNoteSummary>().List();
        });
    }
}
