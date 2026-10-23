using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedWrites;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedWrites;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Separately permits one shadow scalar as fixture state, with explicit expected versions and generation.</summary>
public sealed class RevisionNotebookRepositoryDesigner : RepositoryDesigner
{
    private static readonly bool[] SaveModes = [false, true];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryMappedWrites")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryMappedWrites")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryMappedWrites")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.Entity<RevisionNote>(entity =>
        {
            entity.BindReadPort<IRevisionNoteReader>();
            entity.BindWritePort<IRevisionNoteWriter>();
            entity.HasInMemoryStore<RevisionSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Counter, row.Revision))
                .MapInMemoryEntity<RevisionSeed>(row => new RevisionNote { Id = row.Id, Title = row.Title, Counter = row.Counter })
                .MapInMemoryProperty<RevisionSeed, int>("Revision", row => row.Revision)
                .MapInMemoryWrites<RevisionSeed>((row, saved) => new(row.Id, row.Title, row.Counter, EF.Property<int>(row, "Revision")),
                    useMappedPropertyState: true)
                // A deliberate fixture recipe matching this example's computed column, not inferred rowversion behavior.
                .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Counter + 1);
            entity.HasQuery("List").OrderBy(row => row.Id)
                .Select(row => new RevisionCard(row.Id, row.Title, EF.Property<int>(row, "Revision"))).List();
            foreach (var save in SaveModes)
            {
                var command = entity.HasCommand(save ? "Rename" : "StageRename")
                    .Where<RevisionChange>((row, request) => row.Id == request.Id)
                    .Patch(setters => setters.SetProperty<string, RevisionChange>(row => row.Title, (row, request) => request.Title)
                        .SetProperty(row => row.Counter, row => row.Counter + 1))
                    .ExpectVersion<int, RevisionChange>("Revision", request => request.ExpectedRevision);
                if (save)
                {
                    command.Save().ReturnsSaved(row => new RevisionCard(row.Id, row.Title, EF.Property<int>(row, "Revision")));
                }
                else
                {
                    command.Stage();
                }
            }
        });
    }
}
