using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryValueRepositoryDesigner(bool enabled = true, bool adapters = true) : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.MemoryValues.Contracts")
            .UseGeneratedNamespace("Generated.MemoryValues.Persistence").UseScriptedTestDoubles("Generated.MemoryValues.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands(enabled).RequirePortableTestCommands(enabled).GenerateOnlyConfiguredEntities());
        var entity = modelBuilder.Entity<MemoryValueRow>();
        entity.HasInMemoryStore(row => row.Id, row => new MemoryValueRow { Id = row.Id, Title = row.Title, TitleLength = row.TitleLength, Label = row.Label });
        if (adapters)
        {
            entity.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => checked(existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1));
            entity.HasInMemoryGeneratedValue(row => row.Label, (row, existing) => "fixture");
            entity.HasInMemoryGeneratedValue(row => row.TitleLength, (row, existing) => row.Title.Length);
        }
        entity.HasQuery("List").OrderBy(row => row.Id).Select(row => new MemoryValueView(row.Id, row.Title, row.TitleLength, row.Label)).List();
        foreach (var save in new[] { false, true })
        {
            var create = entity.HasCommand(save ? "Create" : "StageCreate")
                .Create<MemoryValueRequest>(request => new MemoryValueRow { Id = request.Id, Title = request.Title, Label = request.Label }).AllowWrite(row => row.Id).AllowWrite(row => row.Label);
            var patch = entity.HasCommand(save ? "Rename" : "StageRename").Where<MemoryValueRequest>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, MemoryValueRequest>(row => row.Title, (row, request) => request.Title))
                .ExpectVersion<int, MemoryValueRequest>(row => row.TitleLength, (row, request) => request.ExpectedLength);
            if (save)
            {
                create.Save().Returns(row => new MemoryValueView(row.Id, row.Title, row.TitleLength, row.Label));
                patch.Save().Returns(row => new MemoryValueView(row.Id, row.Title, row.TitleLength, row.Label));
            }
            else { create.Stage(); patch.Stage(); }
        }
        entity.HasCommand("RenameMany").PatchMany<MemoryValueRequest, int, IReadOnlyList<MemoryValueRequest>>(items => items, row => row.Id, item => item.Id,
                setters => setters.SetProperty(row => row.Title, (row, item) => item.Title).ExpectVersion(row => row.TitleLength, (row, item) => item.ExpectedLength), maximumItems: 4)
            .Save().Returns(row => new MemoryValueView(row.Id, row.Title, row.TitleLength, row.Label));
        entity.HasCommand("Delete").Where<MemoryValueRequest>((row, request) => row.Id == request.Id)
            .ExpectVersion<int, MemoryValueRequest>(row => row.TitleLength, (row, request) => request.ExpectedLength).TrackedDelete().Save()
            .Returns(row => new MemoryValueView(row.Id, row.Title, row.TitleLength, row.Label));
    }
}
