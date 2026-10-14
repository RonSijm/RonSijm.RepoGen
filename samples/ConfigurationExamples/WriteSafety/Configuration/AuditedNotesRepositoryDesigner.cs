// Paired with ../Model/WriteSafetyContext.cs by ../../GenerationRunner/Program.cs.
// API: ../../Contracts/Generated/WriteSafetyRepositories.g.cs; implementation: ../Generated/Repositories.g.cs.
// See ../README.md for the declaration-to-output map. The consumer never runs this designer.
using RonSijm.RepoGen.ConfigurationExamples.Contracts.WriteSafety;
using RonSijm.RepoGen.ConfigurationExamples.Models.WriteSafety;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class AuditedNotesRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.WriteSafety")
            .GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<AuditedNote>(entity =>
        {
            entity.ReadEnabled = false;
            // These are ordinary EF scalars: their protection is explicit, not inferred from names.
            entity.ProtectWrite(note => note.OwnerId).ProtectWrite(note => note.CreatedBy);
            entity.HasCommand("Create")
                .Create<CreateNoteRequest>(request => new AuditedNote { OwnerId = request.OwnerId, Title = request.Title, CreatedBy = request.CreatedBy })
                .AllowWrite(note => note.OwnerId).AllowWrite(note => note.CreatedBy)
                .Save().Returns(note => new AuditedNoteView(note.Id, note.OwnerId, note.Title, note.CreatedBy, note.Version));
            entity.HasCommand("Rename")
                .Where<RenameNoteRequest>((note, request) => note.Id == request.Id && note.OwnerId == request.OwnerId)
                .Patch(setters => setters.SetProperty<string, RenameNoteRequest>(note => note.Title, (note, request) => request.Title)
                    .SetProperty(note => note.Version, note => note.Version + 1))
                .AllowWrite(note => note.Version)
                .ExpectVersion<int, RenameNoteRequest>(note => note.Version, (note, request) => request.ExpectedVersion)
                .Save().Returns(note => new AuditedNoteView(note.Id, note.OwnerId, note.Title, note.CreatedBy, note.Version));
            // Deliberately distinct operation. The application, not RepoGen, authorizes its callers.
            entity.HasCommand("CorrectAttribution")
                .Where<CorrectAttributionRequest>((note, request) => note.Id == request.Id && note.OwnerId == request.OwnerId)
                .Patch(setters => setters.SetProperty<string, CorrectAttributionRequest>(note => note.CreatedBy, (note, request) => request.CreatedBy)
                    .SetProperty(note => note.Version, note => note.Version + 1))
                .AllowWrite(note => note.CreatedBy).AllowWrite(note => note.Version)
                .ExpectVersion<int, CorrectAttributionRequest>(note => note.Version, (note, request) => request.ExpectedVersion)
                .Save().Returns(note => new AuditedNoteView(note.Id, note.OwnerId, note.Title, note.CreatedBy, note.Version));
        });
    }
}
