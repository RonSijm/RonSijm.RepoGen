// Paired explicitly with ../Model/BatchWritesContext.cs by ../../GenerationRunner/Program.cs.
// API: ../../Contracts/Generated/BatchWritesRepositories.g.cs; implementation: ../Generated/Repositories.g.cs.
using RonSijm.RepoGen.ConfigurationExamples.Contracts.BatchWrites;
using RonSijm.RepoGen.ConfigurationExamples.Models.BatchWrites;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class BatchNotesRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.BatchWrites")
            .GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<BatchNote>(entity =>
        {
            entity.ProtectWrite(note => note.OwnerId);
            entity.HasQuery("ListNotes").Where<int>((note, ownerId) => note.OwnerId == ownerId)
                .OrderBy(note => note.Code).ProjectTo(note => new NoteSummary(note.Code, note.Title, note.Version)).List();
            entity.HasCommand("RenameNotes").Where<EditNotesRequest>((note, request) => note.OwnerId == request.OwnerId)
                .PatchMany<NoteEdit, string, EditNotesRequest>(request => request.Items, note => note.Code, item => item.Code,
                    items => items.SetPropertyWhen(note => note.Title, (note, item) => item.Title.Value, item => item.Title.IsSpecified)
                        .SetProperty(note => note.Version, note => note.Version + 1)
                        .ExpectVersion(note => note.Version, (note, item) => item.ExpectedVersion), maximumItems: 20)
                .AllowWrite(note => note.Version).Save().Returns(note => new NoteSummary(note.Code, note.Title, note.Version));
            entity.HasCommand("StageRenames").Where<EditNotesRequest>((note, request) => note.OwnerId == request.OwnerId)
                .PatchMany<NoteEdit, string, EditNotesRequest>(request => request.Items, note => note.Code, item => item.Code,
                    items => items.SetPropertyWhen(note => note.Title, (note, item) => item.Title.Value, item => item.Title.IsSpecified)
                        .SetProperty(note => note.Version, note => note.Version + 1)
                        .ExpectVersion(note => note.Version, (note, item) => item.ExpectedVersion), maximumItems: 20)
                .AllowWrite(note => note.Version).Stage();
            entity.HasCommand("DeleteNotes").Where<EditNotesRequest>((note, request) => note.OwnerId == request.OwnerId)
                .DeleteMany<NoteEdit, string, EditNotesRequest>(request => request.Items, note => note.Code, item => item.Code,
                    items => items.ExpectVersion(note => note.Version, (note, item) => item.ExpectedVersion), maximumItems: 20)
                .Save().Returns(note => new NoteSummary(note.Code, note.Title, note.Version));
        });
    }
}
