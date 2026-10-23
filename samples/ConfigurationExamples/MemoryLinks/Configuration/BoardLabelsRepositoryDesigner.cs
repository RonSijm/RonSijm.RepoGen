using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryLinks;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryLinks;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Explicit join-row writes and saved read results; existing labels are not aggregate children.</summary>
public sealed class BoardLabelsRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] Operations = ["MergeLabels", "ReplaceLabels", "StageLabels"];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryLinks")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryLinks")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryLinks")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.Entity<StoredBoard>(entity =>
        {
            entity.HasInMemoryStore<BoardSeed, int>(row => row.Id, row => new(row.Id, row.Version))
                .MapInMemoryEntity<BoardSeed>(row => new StoredBoard { Id = row.Id, Version = row.Version })
                .MapInMemoryWrites<BoardSeed>((row, saved) => new(row.Id, row.Version));
            entity.HasInMemoryNavigation(row => row.Labels);
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id)
                .Select(row => new BoardView(row.Id, row.Version, row.Labels.OrderBy(label => label.Id).Select(label => label.Name).ToList())).SingleOrDefault();
            foreach (var operation in Operations)
            {
                var command = entity.HasCommand(operation).Where<ChangeLabels>((row, request) => row.Id == request.BoardId)
                    .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1))
                    .AllowWrite(row => row.Version).ExpectVersion<int, ChangeLabels>(row => row.Version, (row, request) => request.ExpectedVersion)
                    .ReconcileLinks<StoredBoardLabel, LabelInput, int, ChangeLabels>(row => row.Links, request => request.Labels,
                        row => row.LabelId, item => item.LabelId,
                        links => links.MaximumItems(10).Add(item => new StoredBoardLabel { LabelId = item.LabelId, Caption = item.Caption, Version = 1 })
                            .Update(setters => setters.SetProperty(row => row.Caption, (row, item) => item.Caption).SetProperty(row => row.Version, row => row.Version + 1))
                            .AllowWrite(row => row.Version).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                            .MissingItems(operation == "MergeLabels" ? RepositoryCollectionRemovalBehavior.Keep : RepositoryCollectionRemovalBehavior.Delete))
                    .SimulateCollectionWrites();
                if (operation == "StageLabels") command.Stage();
                else command.Save().ReturnsSaved(row => new BoardView(row.Id, row.Version, row.Labels.OrderBy(label => label.Id).Select(label => label.Name).ToList()));
            }
        });
        modelBuilder.Entity<StoredLabel>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<LabelSeed, int>(row => row.Id, row => new(row.Id, row.Name))
                .MapInMemoryEntity<LabelSeed>(row => new StoredLabel { Id = row.Id, Name = row.Name });
        });
        modelBuilder.Entity<StoredBoardLabel>(entity => entity
            .HasInMemoryStore<BoardLabelSeed, (int, int)>(row => new ValueTuple<int, int>(row.BoardId, row.LabelId), row => new(row.BoardId, row.LabelId, row.Caption, row.Version))
            .MapInMemoryEntity<BoardLabelSeed>(row => new StoredBoardLabel { BoardId = row.BoardId, LabelId = row.LabelId, Caption = row.Caption, Version = row.Version })
            .MapInMemoryWrites<BoardLabelSeed>((row, saved) => new(row.BoardId, row.LabelId, row.Caption, row.Version)));
    }
}
