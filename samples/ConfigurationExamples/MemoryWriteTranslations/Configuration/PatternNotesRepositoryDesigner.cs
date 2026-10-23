using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryWriteTranslations;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryWriteTranslations;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class PatternNotesRepositoryDesigner : RepositoryDesigner
{
    private static readonly bool[] SaveModes = [false, true];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryWriteTranslations")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryWriteTranslations")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryWriteTranslations")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.HasInMemoryTranslation<string, string, bool>("OrdinalIgnoreCasePatterns",
            (value, pattern) => EF.Functions.Like(value, pattern),
            (value, pattern) => InMemoryStringPatterns.Like(value, pattern, StringComparison.OrdinalIgnoreCase));
        modelBuilder.HasInMemoryTranslation<string, string>("NormalizedTitle",
            value => PatternNoteFunctions.Normalize(value), value => value.Trim().ToUpperInvariant());
        modelBuilder.HasInMemoryTranslation<string, int>("TitleLength",
            value => PatternNoteFunctions.Length(value), value => value.Length);
        modelBuilder.Entity<StoredPatternNote>(entity =>
        {
            entity.BindReadPort<IPatternNoteReader>().BindWritePort<IPatternNoteWriter>();
            entity.HasInMemoryStore<PatternNoteSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Version, row.Hidden))
                .MapInMemoryEntity<PatternNoteSeed>(row => new StoredPatternNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden })
                .MapInMemoryWrites<PatternNoteSeed>((row, saved) => new(row.Id, row.Title, row.Version, row.Hidden));
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id)
                .Select(row => new PatternNoteCard(row.Id, row.Title, row.Version, PatternNoteFunctions.Length(row.Title))).SingleOrDefault();
            foreach (var save in SaveModes)
            {
                var command = entity.HasCommand(save ? "Rename" : "StageRename")
                    .Validate<PatternNoteRename>(request => request.Id > 0 && PatternNoteFunctions.Normalize(request.Title).Length > 0, "A positive id and title are required.")
                    .Where<PatternNoteRename>((row, request) => row.Id == request.Id && EF.Functions.Like(row.Title, request.Pattern))
                    .Patch(setters => setters.SetProperty<string, PatternNoteRename>(row => row.Title, (row, request) => PatternNoteFunctions.Normalize(request.Title))
                        .SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                    .ExpectVersion<int, PatternNoteRename>(row => row.Version, (row, request) => request.ExpectedVersion)
                    // Registration alone does not opt a command in; each command chooses this independently.
                    .UseInMemoryTranslations();
                if (save) command.Save().ReturnsSaved(row => new PatternNoteCard(row.Id, row.Title, row.Version, PatternNoteFunctions.Length(row.Title)));
                else command.Stage();
            }
            entity.HasCommand("DeleteMatching").Where<string>((row, pattern) => EF.Functions.Like(row.Title, pattern))
                .Delete().SimulateImmediateWrites().UseInMemoryTranslations();
        });
    }
}
