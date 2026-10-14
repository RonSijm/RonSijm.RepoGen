using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryGeneratedValues;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class GeneratedNotebookRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryGeneratedValues")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryGeneratedValues")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryGeneratedValues")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<GeneratedNote>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore(row => row.Id, row => new GeneratedNote
                { Id = row.Id, Text = row.Text, TextLength = row.TextLength, Origin = row.Origin });
            entity.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => checked(existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1));
            entity.HasInMemoryGeneratedValue(row => row.Origin, (row, existing) => "designer");
            entity.HasInMemoryGeneratedValue(row => row.TextLength, (row, existing) => row.Text.Length);
            entity.HasProjection<GeneratedNoteCard>(row => new GeneratedNoteCard(row.Id, row.Text, row.TextLength, row.Origin));
            entity.HasQuery("List").OrderBy(row => row.Id).ProjectTo<GeneratedNoteCard>().List();
            entity.HasCommand("Create").Create<NewGeneratedNote>(request => new GeneratedNote { Text = request.Text, Origin = request.Origin })
                .AllowWrite(row => row.Origin).Save().Returns(row => new GeneratedNoteCard(row.Id, row.Text, row.TextLength, row.Origin));
            entity.HasCommand("StageCreate").Create<NewGeneratedNote>(request => new GeneratedNote { Text = request.Text, Origin = request.Origin })
                .AllowWrite(row => row.Origin).Stage();
            entity.HasCommand("StageRename").Where<RenameGeneratedNote>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, RenameGeneratedNote>(row => row.Text, (row, request) => request.Text))
                .ExpectVersion<int, RenameGeneratedNote>(row => row.TextLength, (row, request) => request.ExpectedLength).Stage();
            entity.HasCommand("RenameMany").PatchMany<RenameGeneratedNote, int, IReadOnlyList<RenameGeneratedNote>>(items => items, row => row.Id, item => item.Id,
                    setters => setters.SetProperty(row => row.Text, (row, item) => item.Text)
                        .ExpectVersion(row => row.TextLength, (row, item) => item.ExpectedLength), maximumItems: 4)
                .Save().Returns(row => new GeneratedNoteCard(row.Id, row.Text, row.TextLength, row.Origin));
        });
    }
}
