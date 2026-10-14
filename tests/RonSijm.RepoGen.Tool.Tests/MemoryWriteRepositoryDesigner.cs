using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryWriteRepositoryDesigner(bool enabled = true) : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.MemoryWrites.Contracts")
            .UseGeneratedNamespace("Generated.MemoryWrites.Persistence").UseScriptedTestDoubles("Generated.MemoryWrites.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands(enabled).RequirePortableTestCommands(enabled)
            .GenerateOnlyConfiguredEntities());
        Configure(modelBuilder);
    }
    internal static void Configure(RepositoryModelBuilder builder)
    {
        var note = builder.Entity<MemoryWriteNote>();
        note.HasInMemoryStore(row => row.Id, row => new MemoryWriteNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden });
        note.HasVisibilityPolicy("Visible", row => !row.Hidden);
        note.HasQuery("Find").Where<int>((row, id) => row.Id == id).Select(row => new MemoryWriteView(row.Id, row.Title, row.Version)).SingleOrDefault();
        note.HasCommand("Create").Create<MemoryWriteRequest>(request => new MemoryWriteNote { Id = request.Id, Title = request.Title, Version = 1 })
            .AllowWrite(row => row.Id).AllowWrite(row => row.Version).Validate<MemoryWriteRequest>(request => request.Id > 0, "Positive id required.")
            .Save().Returns(row => new MemoryWriteView(row.Id, row.Title, row.Version));
        foreach (var save in new[] { false, true })
        {
            var patch = note.HasCommand(save ? "Rename" : "StageRename").Where<MemoryWriteRequest>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetPropertyWhen<string, MemoryWriteRequest>(row => row.Title, (row, request) => request.Title, request => request.Supplied)
                    .SetPropertyWhen<int, MemoryWriteRequest>(row => row.Version, (row, request) => row.Version + 1, request => request.Supplied))
                .AllowWrite(row => row.Version).ExpectVersion<int, MemoryWriteRequest>(row => row.Version, (row, request) => request.ExpectedVersion);
            if (save) patch.Save().Returns(row => new MemoryWriteView(row.Id, row.Title, row.Version)); else patch.Stage();
        }
        note.HasCommand("Delete").Where<MemoryWriteRequest>((row, request) => row.Id == request.Id).TrackedDelete()
            .ExpectVersion<int, MemoryWriteRequest>(row => row.Version, (row, request) => request.ExpectedVersion).Save()
            .Returns(row => new MemoryWriteView(row.Id, row.Title, row.Version));
        note.HasCommand("StageDelete").Where<int>((row, id) => row.Id == id).TrackedDelete().Stage();
        var audit = builder.Entity<MemoryWriteAudit>();
        audit.ReadEnabled = false;
        audit.HasInMemoryStore(row => row.Id, row => new MemoryWriteAudit { Id = row.Id, Text = row.Text });
        audit.HasCommand("StageAudit").Create<MemoryWriteRequest>(request => new MemoryWriteAudit { Id = request.Id, Text = request.Title }).AllowWrite(row => row.Id).Stage();
    }
}
