// Designer -> ../Model/MemoryNotebookContext.cs -> ../Generated/TestDoubles.g.cs and shared contracts.
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNotebook;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class MemoryNotebookRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryNotebook")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryNotebook")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryNotebook")
            .UseInMemoryTestStores().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<MemoryNote>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<IMemoryNoteLookup>();
            entity.HasInMemoryStore(note => new NoteKey(note.Id),
                note => new MemoryNote { Id = note.Id, Name = note.Name, Labels = note.Labels.ToList() });
            entity.HasProjection(note => new MemoryNoteSummary(note.Id, note.Name));
            entity.HasQuery("Find").Where<int>((note, id) => note.Id == id).ProjectTo<MemoryNoteSummary>().SingleOrDefault();
        });
        modelBuilder.Entity<MemoryLabel>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore(label => label.Code, label => new MemoryLabel { Code = label.Code, Text = label.Text });
            entity.HasQuery("Count").Count();
        });
    }
}
