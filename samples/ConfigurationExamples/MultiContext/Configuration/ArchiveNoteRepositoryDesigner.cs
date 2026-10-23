// Paired with ArchiveNoteContext in ../Model/NoteStores.cs by ../../GenerationRunner/Program.cs.
// Read API: ../../Contracts/Generated/MultiContext/RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ArchiveNotes.IReadStoredNoteRepository.g.cs.
// Implementation: ../Generated/RonSijm.RepoGen.ConfigurationExamples.MultiContext.Archive.StoredNote.Repository.g.cs.
// See ../README.md for exact filenames and the declaration-to-output map; the consumer does not run this designer.
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.MultiContext;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class ArchiveNoteRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ArchiveNotes")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.MultiContext.Archive")
            .GenerateOnlyConfiguredEntities().IncludeApiDataContracts());
        modelBuilder.Entity<StoredNote>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<INoteArchiveReader>();
            entity.HasProjection<StoredNoteSummary>(note => new StoredNoteSummary(note.Id, note.Text));
            entity.HasQuery("ListNotes").OrderBy(note => note.Id).ProjectTo<StoredNoteSummary>().List();
        });
    }
}
