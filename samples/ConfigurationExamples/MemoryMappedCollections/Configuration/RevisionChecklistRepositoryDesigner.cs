using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedCollections;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedCollections;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Explicit scalar state on both sides of bounded collection reconciliation, without shadow identity.</summary>
public sealed class RevisionChecklistRepositoryDesigner : RepositoryDesigner
{
    private static readonly bool[] SaveModes = [false, true];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryMappedCollections")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryMappedCollections")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryMappedCollections")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.Entity<VersionedChecklist>(entity =>
        {
            entity.ReadEnabled = false;
            entity.BindWritePort<IVersionedChecklistWriter>();
            entity.HasInMemoryStore<VersionedChecklistSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Counter, row.Revision))
                .MapInMemoryEntity<VersionedChecklistSeed>(row => new VersionedChecklist { Id = row.Id, Title = row.Title, Counter = row.Counter })
                .MapInMemoryProperty<VersionedChecklistSeed, int>("Revision", row => row.Revision)
                .MapInMemoryWrites<VersionedChecklistSeed>((row, saved) => new(row.Id, row.Title, row.Counter, EF.Property<int>(row, "Revision")),
                    useMappedPropertyState: true)
                // These deliberate recipes match this example's computed columns; no rowversion behavior is inferred.
                .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Counter + 1);
            foreach (var save in SaveModes)
            {
                var command = entity.HasCommand(save ? "Replace" : "Stage")
                    .Where<VersionedChecklistChange>((row, request) => row.Id == request.Id)
                    .Patch(setters => setters.SetProperty<string, VersionedChecklistChange>(row => row.Title, (row, request) => request.Title)
                        .SetProperty(row => row.Counter, row => row.Counter + 1))
                    .ExpectVersion<int, VersionedChecklistChange>("Revision", request => request.ExpectedRevision)
                    .ReconcileCollection<VersionedChecklistItem, VersionedItemChange, string, VersionedChecklistChange>(row => row.Items, request => request.Items,
                        row => row.Code, item => item.Code, children => children.MaximumItems(10).AllowWrite(row => row.Id)
                            .Add(item => new VersionedChecklistItem { Id = item.Id, Code = item.Code, Text = item.Text, Counter = 0 })
                            .Update(setters => setters.SetProperty(row => row.Text, (row, item) => item.Text)
                                .SetProperty(row => row.Counter, row => row.Counter + 1))
                            .ExpectVersion<int>("Revision", item => item.ExpectedRevision)
                            .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
                    .SimulateCollectionWrites();
                if (save)
                {
                    command.Save().ReturnsSaved(row => EF.Property<int>(row, "Revision"));
                }
                else
                {
                    command.Stage();
                }
            }
        });
        modelBuilder.Entity<VersionedChecklistItem>(entity =>
        {
            // A writable fixture store is required for reconciliation even without child write-port methods.
            entity.BindReadPort<IVersionedChecklistItems>();
            entity.HasInMemoryStore<VersionedItemSeed, int>(row => row.Id,
                    row => new(row.Id, row.ChecklistId, row.Code, row.Text, row.Counter, row.Revision))
                .MapInMemoryEntity<VersionedItemSeed>(row => new VersionedChecklistItem
                    { Id = row.Id, ChecklistId = row.ChecklistId, Code = row.Code, Text = row.Text, Counter = row.Counter })
                .MapInMemoryProperty<VersionedItemSeed, int>("Revision", row => row.Revision)
                .MapInMemoryWrites<VersionedItemSeed>((row, saved) => new(row.Id, row.ChecklistId, row.Code, row.Text, row.Counter, EF.Property<int>(row, "Revision")),
                    useMappedPropertyState: true)
                .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Counter + 1);
            entity.HasQuery("List").Where<int>((row, checklistId) => row.ChecklistId == checklistId).OrderBy(row => row.Code)
                .Select(row => new VersionedItemCard(row.Id, row.Code, row.Text, EF.Property<int>(row, "Revision"))).List();
        });
    }
}
