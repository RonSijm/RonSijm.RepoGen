using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedBatchSetters;
using RonSijm.RepoGen.ConfigurationExamples.Models.MappedBatchSetters;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>One bounded batch recipe; services receive neither setters, model names nor EF queries.</summary>
public sealed class ReviewEntriesRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] Commands = ["Revise", "StageRevise"];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MappedBatchSetters")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MappedBatchSetters")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MappedBatchSetters")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestCommands());
        modelBuilder.Entity<ReviewEntry>(entity =>
        {
            entity.BindReadPort<IReviewEntryReader>().BindWritePort<IReviewEntryWriter>();
            entity.ProtectWrite<string?>(ReviewEntriesContext.Remark);
            entity.HasInMemoryStore<ReviewEntryCard, int>(row => row.Id, row => new(row.Id, row.Label, row.Caption, row.Edits, row.Revision, row.Remark))
                .MapInMemoryEntity<ReviewEntryCard>(row => new ReviewEntry { Id = row.Id, Label = row.Label })
                .MapInMemoryProperty<ReviewEntryCard, string>(nameof(ReviewEntry.Caption), row => row.Caption)
                .MapInMemoryProperty<ReviewEntryCard, int>("edits", row => row.Edits)
                .MapInMemoryProperty<ReviewEntryCard, int>(ReviewEntriesContext.Revision, row => row.Revision)
                .MapInMemoryProperty<ReviewEntryCard, string?>(ReviewEntriesContext.Remark, row => row.Remark)
                .MapInMemoryWrites<ReviewEntryCard>((row, saved) => new(row.Id, row.Label, row.Caption, EF.Property<int>(row, "edits"),
                    EF.Property<int>(row, ReviewEntriesContext.Revision), (string?)row[ReviewEntriesContext.Remark]), useMappedPropertyState: true);
            entity.HasQuery("Cards").OrderBy(row => row.Id).Select(row => new ReviewEntryCard(row.Id, row.Label, row.Caption,
                EF.Property<int>(row, "edits"), EF.Property<int>(row, ReviewEntriesContext.Revision), (string?)row[ReviewEntriesContext.Remark])).List();
            foreach (var name in Commands)
            {
                var command = entity.HasCommand(name)
                    .PatchMany<ReviewEntryEdit, int, ReviewEntriesRequest>(request => request.Entries, row => row.Id, item => item.Id,
                        setters => setters
                            .SetPropertyWhen<string>(nameof(ReviewEntry.Caption), (row, item) => item.Caption, item => item.ChangeCaption)
                            .SetProperty<int>("edits", row => EF.Property<int>(row, "edits") + 1)
                            .SetProperty<int>(ReviewEntriesContext.Revision, (row, item) => item.Revision + 1)
                            .SetProperty<string?>(ReviewEntriesContext.Remark, (row, item) => item.Remark)
                            .ExpectVersion<int>(ReviewEntriesContext.Revision, item => item.Revision), maximumItems: 20)
                    .AllowWrite<int>(ReviewEntriesContext.Revision).AllowWrite<string?>(ReviewEntriesContext.Remark);
                if (name == "StageRevise") command.Stage();
                else command.Save().ReturnsSaved(row => new ReviewEntryCard(row.Id, row.Label, row.Caption, EF.Property<int>(row, "edits"),
                    EF.Property<int>(row, ReviewEntriesContext.Revision), (string?)row[ReviewEntriesContext.Remark]));
            }
        });
    }
}
