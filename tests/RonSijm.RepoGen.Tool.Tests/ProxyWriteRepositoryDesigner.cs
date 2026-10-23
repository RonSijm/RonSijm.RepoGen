using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProxyWriteRepositoryDesigner(bool commands = true, bool reads = true) : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.ProxyWrites.Contracts")
            .UseGeneratedNamespace("Generated.ProxyWrites.Persistence").UseScriptedTestDoubles("Generated.ProxyWrites.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestQueries(reads).UseInMemoryTestCommands(commands).RequirePortableTestCommands(commands).GenerateOnlyConfiguredEntities());
        Configure(modelBuilder);
    }

    internal static RepositoryEntityBuilder<ProxyWriteRecord> Store(RepositoryModelBuilder builder)
    {
        var entity = builder.Entity<ProxyWriteRecord>();
        entity.HasInMemoryStore<ProxyWriteData, int>(row => row.Key, row => new ProxyWriteData(row.Key, row.Caption, row.Revision, row.Label, row.Archived, row.Binary.ToArray(), row.Extra))
            .MapInMemoryEntity<ProxyWriteData>(row => new ProxyWriteRecord { Id = row.Key, Name = row.Caption, Version = row.Revision, Label = row.Label, Hidden = row.Archived, Stamp = row.Binary })
            .MapInMemoryWrites<ProxyWriteData>((row, saved) => new ProxyWriteData(row.Id, row.Name, row.Version, row.Label, row.Hidden, row.Stamp.ToArray(), saved == null ? "created" : saved.Extra))
            .HasInMemoryGeneratedValue(row => row.Id, (row, existing) => checked(existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1))
            .HasInMemoryGeneratedValue(row => row.Label, (row, existing) => "fixture")
            .HasInMemoryGeneratedValue(row => row.Version, (row, existing) => row.Name.Length);
        entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
        return entity;
    }

    internal static void Configure(RepositoryModelBuilder builder)
    {
        var entity = Store(builder);
        entity.HasQuery("List").OrderBy(row => row.Id).Select(row => new ProxyWriteView(row.Id, row.Name, row.Version, row.Label, row.Stamp.ToArray())).List();
        foreach (var save in new[] { false, true })
        {
            var create = entity.HasCommand(save ? "Create" : "StageCreate").Create<ProxyWriteRequest>(request => new ProxyWriteRecord
                { Id = request.Id, Name = request.Name, Label = request.Label, Hidden = false, Stamp = request.Stamp ?? Array.Empty<byte>() }).AllowWrite(row => row.Id).AllowWrite(row => row.Label);
            var patch = entity.HasCommand(save ? "Rename" : "StageRename").Where<ProxyWriteRequest>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetPropertyWhen<string, ProxyWriteRequest>(row => row.Name, (row, request) => request.Name, request => request.Supplied)
                    .SetPropertyWhen<byte[], ProxyWriteRequest>(row => row.Stamp, (row, request) => request.Stamp!, request => request.Stamp != null))
                .ExpectVersion<int, ProxyWriteRequest>(row => row.Version, (row, request) => request.ExpectedVersion);
            if (save)
            {
                create.Save().Returns(row => new ProxyWriteView(row.Id, row.Name, row.Version, row.Label, row.Stamp.ToArray()));
                patch.Save().Returns(row => new ProxyWriteView(row.Id, row.Name, row.Version, row.Label, row.Stamp.ToArray()));
            }
            else { create.Stage(); patch.Stage(); }
        }
        entity.HasCommand("RenameMany").PatchMany<ProxyWriteRequest, int, IReadOnlyList<ProxyWriteRequest>>(items => items, row => row.Id, item => item.Id,
            setters => setters.SetProperty(row => row.Name, (row, item) => item.Name).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion), maximumItems: 4)
            .Save().Returns(row => new ProxyWriteView(row.Id, row.Name, row.Version, row.Label, row.Stamp.ToArray()));
        foreach (var save in new[] { false, true })
        {
            var delete = entity.HasCommand(save ? "Delete" : "StageDelete").Where<ProxyWriteRequest>((row, request) => row.Id == request.Id)
                .ExpectVersion<int, ProxyWriteRequest>(row => row.Version, (row, request) => request.ExpectedVersion).TrackedDelete();
            if (save) delete.Save().Returns(row => new ProxyWriteView(row.Id, row.Name, row.Version, row.Label, row.Stamp.ToArray())); else delete.Stage();
        }
        entity.HasCommand("DeleteMany").DeleteMany<ProxyWriteRequest, int, IReadOnlyList<ProxyWriteRequest>>(items => items, row => row.Id, item => item.Id,
            expected => expected.ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion), maximumItems: 4).Save()
            .Returns(row => new ProxyWriteView(row.Id, row.Name, row.Version, row.Label, row.Stamp.ToArray()));
        var audit = builder.Entity<ProxyWriteAudit>(); audit.ReadEnabled = false;
        audit.HasInMemoryStore<ProxyWriteAuditData, int>(row => row.Key, row => new ProxyWriteAuditData(row.Key, row.Caption))
            .MapInMemoryEntity<ProxyWriteAuditData>(row => new ProxyWriteAudit { Id = row.Key, Text = row.Caption })
            .MapInMemoryWrites<ProxyWriteAuditData>((row, saved) => new ProxyWriteAuditData(row.Id, row.Text));
        audit.HasCommand("StageAudit").Create<ProxyWriteRequest>(request => new ProxyWriteAudit { Id = request.Id, Text = request.Name }).AllowWrite(row => row.Id).Stage();
    }
}
