// Paired with ../Model/ScriptedNotesContext.cs by ../../GenerationRunner/Program.cs.
// API: ../../Contracts/Generated/ScriptedNotesRepositories.g.cs; test fixture: ../Generated/TestDoubles.g.cs.
using RonSijm.RepoGen.ConfigurationExamples.Contracts.ScriptedNotes;
using RonSijm.RepoGen.ConfigurationExamples.Models.ScriptedNotes;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class ScriptedNotesRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ScriptedNotes")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.ScriptedNotes")
            .GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<ScriptedNote>(entity =>
        {
            entity.BindReadPort<INoteLookup>().BindWritePort<INoteWriter>();
            entity.HasProjection(note => new NoteSummary(note.Id, note.Name));
            entity.HasQuery("Find").Where<int>((note, id) => note.Id == id).ProjectTo<NoteSummary>().SingleOrDefault();
            entity.HasQuery("Find").Where<string>((note, name) => note.Name == name).ProjectTo<NoteSummary>().SingleOrDefault();
            entity.HasQuery("ListSelected").Where<SelectedNotes>((note, request) => request.Ids.Contains(note.Id))
                .OrderBy(note => note.Id).ProjectTo<NoteSummary>().List();
            entity.HasCommand("Create").Create<CreateNoteRequest>(request => new ScriptedNote { Name = request.Name })
                .Save().Returns(note => new NoteSummary(note.Id, note.Name));
        });
    }
}
