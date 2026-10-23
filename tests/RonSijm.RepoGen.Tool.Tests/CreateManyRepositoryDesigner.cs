using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CreateManyRepositoryDesigner(bool mapped = false, bool backend = true, bool keyed = false, bool separate = true) : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.CreateMany.Contracts")
            .UseScriptedTestDoubles("Generated.CreateMany.TestDoubles").UseInMemoryTestStores().UseInMemoryTestQueries()
            .UseInMemoryTestCommands(backend).RequirePortableTestCommands(backend).GenerateOnlyConfiguredEntities());
        if (separate) modelBuilder.Configure(options => options.UseGeneratedNamespace("Generated.CreateMany.Persistence"));
        if (keyed) modelBuilder.Configure(options => options.UseKeyedContextLeases<string>());
        var entity = modelBuilder.Entity<CreateManyRow>();
        if (mapped)
            entity.HasInMemoryStore<CreateManyData, int>(row => row.Id, row => new(row.Id, row.ParentId, row.Name, row.Label, row.Hidden))
                .MapInMemoryEntity<CreateManyData>(row => new CreateManyRow { Id = row.Id, ParentId = row.ParentId, Name = row.Name, Label = row.Label, Hidden = row.Hidden })
                .MapInMemoryWrites<CreateManyData>((row, saved) => new(row.Id, row.ParentId, row.Name, row.Label, row.Hidden));
        else
            entity.HasInMemoryStore(row => row.Id, row => new CreateManyRow { Id = row.Id, ParentId = row.ParentId, Name = row.Name, Label = row.Label, Hidden = row.Hidden });
        entity.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => checked(existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1));
        entity.HasInMemoryGeneratedValue(row => row.Label, (row, existing) => "fixture");
        entity.HasInMemoryNavigation(row => row.Parent);
        entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
        foreach (var name in new[] { "Create", "Fresh", "Stage", "Count", "Fail", "Bypass" })
        {
            var command = entity.HasCommand(name).CreateMany<CreateManyItem, CreateManyRequest>(request => request.Items,
                item => new CreateManyRow { ParentId = item.ParentId, Name = item.Name.Trim(), Hidden = item.Hidden }, maximumItems: 3)
                .AllowWrite(row => row.ParentId);
            if (keyed) command.UseContextKey<CreateManyRequest, string>(request => request.Partition);
            if (name == "Stage") command.Stage();
            else
            {
                command.Save();
                if (name == "Create") command.Returns(row => row.Id);
                else if (name == "Fail") command.ReturnsSaved(row => new SavedResultRejectedView(row.Name));
                else if (name != "Count") command.ReturnsSaved<CreateManyView, CreateManyRequest>((row, request) =>
                    new CreateManyView(row.Id, row.Name, row.Label, row.Parent == null ? "missing" : row.Parent.Name, request.Label));
                if (name == "Bypass") command.BypassVisibility("Visible");
            }
        }
        var parent = modelBuilder.Entity<CreateManyParent>();
        parent.HasInMemoryStore(row => row.Id, row => new CreateManyParent { Id = row.Id, Name = row.Name });
        if (!keyed) parent.HasCommand("StageName").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(row => row.Name, "shared")).Stage();
    }
}
