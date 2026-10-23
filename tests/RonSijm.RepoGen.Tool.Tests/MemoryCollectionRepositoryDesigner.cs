using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryCollectionRepositoryDesigner(bool simulate = true, bool backend = true, bool strict = true, bool childStore = true, bool generatedValues = true) : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.MemoryCollections.Contracts")
            .UseGeneratedNamespace("Generated.MemoryCollections.Persistence").UseScriptedTestDoubles("Generated.MemoryCollections.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands(backend).RequirePortableTestCommands(strict && backend).GenerateOnlyConfiguredEntities());
        var parent = modelBuilder.Entity<MemoryCollectionParent>();
        parent.HasInMemoryStore(row => row.Id, row => new MemoryCollectionParent { Id = row.Id, OwnerId = row.OwnerId, Name = row.Name, Version = row.Version });
        parent.HasQuery("Find").Where<int>((row, id) => row.Id == id).Select(row => new MemoryCollectionView(row.Id, row.Name, row.Version)).SingleOrDefault();
        foreach (var name in new[] { "SaveItems", "StageItems", "KeepItems", "RejectItems" })
        {
            var command = parent.HasCommand(name).Where<MemoryCollectionRequest>((row, request) => row.Id == request.Id && row.OwnerId == request.OwnerId)
                .Patch(setters => setters.SetProperty<string, MemoryCollectionRequest>(row => row.Name, (row, request) => request.Name)
                    .SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                .ExpectVersion<int, MemoryCollectionRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
                .ReconcileCollection<MemoryCollectionChild, MemoryCollectionItem, string, MemoryCollectionRequest>(row => row.Children, request => request.Items!, row => row.Key, item => item.Key,
                    children => children.MaximumItems(3).Add(item => new MemoryCollectionChild { Key = item.Key, Text = item.Text, Version = 1, Hidden = false })
                        .Update(setters => setters.SetPropertyWhen(row => row.Text, (row, item) => item.Text, item => item.Supplied)
                            .SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                        .ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                        .MissingItems(name == "KeepItems" ? RepositoryCollectionRemovalBehavior.Keep : name == "RejectItems" ? RepositoryCollectionRemovalBehavior.Reject : RepositoryCollectionRemovalBehavior.Delete))
                .SimulateCollectionWrites(simulate);
            if (name == "StageItems") command.Stage(); else command.Save().Returns(row => new MemoryCollectionView(row.Id, row.Name, row.Version));
        }
        parent.HasCommand("OptionalItems").Where<MemoryCollectionOptionalRequest>((row, request) => row.Id == request.Id)
            .ReconcileCollection<MemoryCollectionChild, MemoryCollectionItem, string, MemoryCollectionOptionalRequest>(row => row.Children, request => request.Items.Value!, row => row.Key, item => item.Key,
                children => children.MaximumItems(3).When<MemoryCollectionOptionalRequest>(request => request.Items.IsSpecified)
                    .Update(setters => setters.SetPropertyWhen(row => row.Text, (row, item) => item.Text, item => item.Supplied))
                    .MissingItems(RepositoryCollectionRemovalBehavior.Delete)).Save().SimulateCollectionWrites(simulate);
        var child = modelBuilder.Entity<MemoryCollectionChild>();
        if (childStore) child.HasInMemoryStore(row => row.Id, row => new MemoryCollectionChild { Id = row.Id, ParentId = row.ParentId, Key = row.Key, Text = row.Text, Version = row.Version, Hidden = row.Hidden });
        if (childStore && generatedValues) child.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1);
        child.HasVisibilityPolicy("Visible", row => !row.Hidden);
        child.HasQuery("List").Where<int>((row, parentId) => row.ParentId == parentId).OrderBy(row => row.Key).Select(row => row.Text).List();
        if (childStore) child.HasCommand("StageText").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Text, "pending")).Stage();
    }
}
