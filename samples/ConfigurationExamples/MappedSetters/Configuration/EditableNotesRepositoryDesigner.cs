using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedSetters;
using RonSijm.RepoGen.ConfigurationExamples.Models.MappedSetters;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Explicit mapped setter targets, not public persistence setters or a service-side EF DSL.</summary>
public sealed class EditableNotesRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] Commands = ["Edit", "StageEdit", "ExecuteEdit"];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MappedSetters")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MappedSetters")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MappedSetters")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestCommands());
        modelBuilder.Entity<EditableNote>(entity =>
        {
            entity.BindReadPort<IEditableNoteReader>().BindWritePort<IEditableNoteWriter>();
            entity.ProtectWrite<string?>(EditableNotesContext.Remark);
            entity.HasInMemoryStore<EditableNoteSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Caption, row.Revision, row.Remark))
                .MapInMemoryEntity<EditableNoteSeed>(row => new EditableNote { Id = row.Id, Title = row.Title })
                .MapInMemoryProperty<EditableNoteSeed, string>(nameof(EditableNote.Caption), row => row.Caption)
                .MapInMemoryProperty<EditableNoteSeed, int>(EditableNotesContext.Revision, row => row.Revision)
                .MapInMemoryProperty<EditableNoteSeed, string?>(EditableNotesContext.Remark, row => row.Remark)
                .MapInMemoryWrites<EditableNoteSeed>((row, saved) => new(row.Id, row.Title, row.Caption,
                    EF.Property<int>(row, EditableNotesContext.Revision), (string?)row[EditableNotesContext.Remark]), useMappedPropertyState: true);
            entity.HasProjection(row => new EditableNoteSeed(row.Id, row.Title, row.Caption,
                EF.Property<int>(row, EditableNotesContext.Revision), (string?)row[EditableNotesContext.Remark]));
            entity.HasQuery("Cards").OrderBy(row => row.Id).ProjectTo<EditableNoteSeed>().List();
            foreach (var name in Commands)
            {
                var command = entity.HasCommand(name).Where<EditNoteRequest>((row, request) => row.Id == request.Id)
                    .ExpectVersion<int, EditNoteRequest>(EditableNotesContext.Revision, request => request.Version)
                    .AllowWrite<int>(EditableNotesContext.Revision)
                    .AllowWrite<string?>(EditableNotesContext.Remark);
                void Set(RepositoryUpdateBuilder<EditableNote> setters)
                {
                    if (name == "ExecuteEdit")
                        setters.SetProperty<string, EditNoteRequest>(nameof(EditableNote.Caption), (row, request) => request.Caption);
                    else setters.SetPropertyWhen<string, EditNoteRequest>(nameof(EditableNote.Caption),
                        (row, request) => request.Caption, request => request.ChangeCaption);
                    setters.SetProperty<int, EditNoteRequest>(EditableNotesContext.Revision, (row, request) => request.Version + 1)
                        .SetProperty<string?, EditNoteRequest>(EditableNotesContext.Remark, (row, request) => request.Remark);
                }
                if (name == "ExecuteEdit") command.Update(Set).SimulateImmediateWrites();
                else
                {
                    command.Patch(Set);
                    if (name == "StageEdit") command.Stage();
                    else command.Save().ReturnsSaved(row => new EditableNoteSeed(row.Id, row.Title, row.Caption,
                        EF.Property<int>(row, EditableNotesContext.Revision), (string?)row[EditableNotesContext.Remark]));
                }
            }
        });
    }
}
