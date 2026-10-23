using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryBatchRepositoryDesigner(bool enabled = true) : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.MemoryBatches.Contracts")
            .UseGeneratedNamespace("Generated.MemoryBatches.Persistence").UseScriptedTestDoubles("Generated.MemoryBatches.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands(enabled).RequirePortableTestCommands(enabled).GenerateOnlyConfiguredEntities());
        var note = modelBuilder.Entity<MemoryWriteNote>();
        note.HasInMemoryStore(row => row.Id, row => new MemoryWriteNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden });
        note.HasVisibilityPolicy("Visible", row => !row.Hidden);
        note.HasQuery("List").OrderBy(row => row.Id).Select(row => new MemoryWriteView(row.Id, row.Title, row.Version)).List();
        foreach (var save in new[] { false, true })
        {
            var command = note.HasCommand(save ? "RenameMany" : "StageMany")
                .PatchMany<MemoryWriteRequest, int, IReadOnlyList<MemoryWriteRequest>>(items => items, row => row.Id, item => item.Id,
                    setters => setters.SetPropertyWhen(row => row.Title, (row, item) => item.Title, item => item.Supplied)
                        .SetProperty(row => row.Version, row => row.Version + 1).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion), maximumItems: 3)
                .AllowWrite(row => row.Version);
            if (save) command.Save().Returns(row => new MemoryWriteView(row.Id, row.Title, row.Version)); else command.Stage();
        }
        note.HasCommand("DeleteMany").DeleteMany<MemoryWriteRequest, int, IReadOnlyList<MemoryWriteRequest>>(items => items, row => row.Id, item => item.Id,
            setters => setters.ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion), maximumItems: 3).Save()
            .Returns(row => new MemoryWriteView(row.Id, row.Title, row.Version));
        note.HasCommand("Unversioned").PatchMany<MemoryWriteRequest, int, IReadOnlyList<MemoryWriteRequest>>(items => items, row => row.Id, item => item.Id,
            setters => setters.SetPropertyWhen(row => row.Title, (row, item) => item.Title, item => item.Supplied), maximumItems: 3).Save()
            .Returns(row => new MemoryWriteView(row.Id, row.Title, row.Version));
        var audit = modelBuilder.Entity<MemoryWriteAudit>();
        audit.ReadEnabled = false;
        audit.HasInMemoryStore(row => row.Id, row => new MemoryWriteAudit { Id = row.Id, Text = row.Text });
        audit.HasCommand("StageAudit").Create<MemoryWriteRequest>(request => new MemoryWriteAudit { Id = request.Id, Text = request.Title }).AllowWrite(row => row.Id).Stage();
    }
}
