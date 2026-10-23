using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedCollectionSetters;
using RonSijm.RepoGen.ConfigurationExamples.Models.MappedCollectionSetters;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Bounded mapped parent/child updates; ownership, persistence recipes and permissions stay out of services.</summary>
public sealed class ReviewBoardsRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] Commands = ["Merge", "Replace", "StageReplace"];
    private static Expression<Func<ReviewBoard, ReviewBoardView>> Projection => row => new ReviewBoardView(row.Id, row.Name,
        EF.Property<int>(row, ReviewBoardsContext.Revision), row.Items.OrderBy(item => item.Code)
            .Select(item => new ReviewItemSeed(item.Id, item.BoardId, item.Code, item.Caption, EF.Property<int>(item, "edits"),
                EF.Property<int>(item, ReviewBoardsContext.Revision), (string?)item[ReviewBoardsContext.Remark])).ToList());

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MappedCollectionSetters")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MappedCollectionSetters")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MappedCollectionSetters")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestCommands());
        var board = modelBuilder.Entity<ReviewBoard>();
        board.BindReadPort<IReviewBoardReader>().BindWritePort<IReviewBoardWriter>();
        board.HasInMemoryStore<ReviewBoardSeed, int>(row => row.Id, row => new(row.Id, row.Name, row.Revision))
            .MapInMemoryEntity<ReviewBoardSeed>(row => new ReviewBoard { Id = row.Id, Name = row.Name })
            .MapInMemoryProperty<ReviewBoardSeed, int>(ReviewBoardsContext.Revision, row => row.Revision)
            .MapInMemoryWrites<ReviewBoardSeed>((row, saved) => new(row.Id, row.Name, EF.Property<int>(row, ReviewBoardsContext.Revision)), useMappedPropertyState: true);
        board.HasInMemoryNavigation(row => row.Items);
        board.HasQuery("Boards").OrderBy(row => row.Id).Select(Projection).List();
        foreach (var name in Commands)
        {
            var command = board.HasCommand(name).Where<ReviewBoardEdit>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, ReviewBoardEdit>(row => row.Name, (row, request) => request.Name)
                    .SetProperty<int, ReviewBoardEdit>(ReviewBoardsContext.Revision, (row, request) => request.Revision + 1))
                .AllowWrite<int>(ReviewBoardsContext.Revision).ExpectVersion<int, ReviewBoardEdit>(ReviewBoardsContext.Revision, request => request.Revision)
                .ReconcileCollection<ReviewItem, ReviewItemEdit, string, ReviewBoardEdit>(row => row.Items, request => request.Items,
                    item => item.Code, item => item.Code, items => items.MaximumItems(10).When<ReviewBoardEdit>(request => request.IncludeItems)
                        .Update(setters => setters
                            .SetPropertyWhen<string>(nameof(ReviewItem.Caption), (item, edit) => edit.Caption, edit => edit.ChangeCaption)
                            .SetProperty<int>("edits", item => EF.Property<int>(item, "edits") + 1)
                            .SetProperty<int>(ReviewBoardsContext.Revision, (item, edit) => edit.Revision + 1)
                            .SetProperty<string?>(ReviewBoardsContext.Remark, (item, edit) => edit.Remark))
                        .AllowWrite<int>(ReviewBoardsContext.Revision).AllowWrite<string?>(ReviewBoardsContext.Remark)
                        .ExpectVersion<int>(ReviewBoardsContext.Revision, edit => edit.Revision)
                        .MissingItems(name == "Merge" ? RepositoryCollectionRemovalBehavior.Keep : RepositoryCollectionRemovalBehavior.Delete))
                .SimulateCollectionWrites();
            if (name == "StageReplace") command.Stage();
            else command.Save().ReturnsSaved(Projection);
        }
        var item = modelBuilder.Entity<ReviewItem>();
        item.ProtectWrite<string?>(ReviewBoardsContext.Remark);
        item.HasInMemoryStore<ReviewItemSeed, int>(row => row.Id, row => new(row.Id, row.BoardId, row.Code, row.Caption, row.Edits, row.Revision, row.Remark))
            .MapInMemoryEntity<ReviewItemSeed>(row => new ReviewItem { Id = row.Id, BoardId = row.BoardId, Code = row.Code })
            .MapInMemoryProperty<ReviewItemSeed, string>(nameof(ReviewItem.Caption), row => row.Caption)
            .MapInMemoryProperty<ReviewItemSeed, int>("edits", row => row.Edits)
            .MapInMemoryProperty<ReviewItemSeed, int>(ReviewBoardsContext.Revision, row => row.Revision)
            .MapInMemoryProperty<ReviewItemSeed, string?>(ReviewBoardsContext.Remark, row => row.Remark)
            .MapInMemoryWrites<ReviewItemSeed>((row, saved) => new(row.Id, row.BoardId, row.Code, row.Caption, EF.Property<int>(row, "edits"),
                EF.Property<int>(row, ReviewBoardsContext.Revision), (string?)row[ReviewBoardsContext.Remark]), useMappedPropertyState: true);
    }
}
