using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryProxyWrites;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryProxyWrites;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class ProxyNotebookRepositoryDesigner : RepositoryDesigner
{
    private static readonly bool[] SaveModes = [false, true];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryProxyWrites")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryProxyWrites")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryProxyWrites")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<PrivateNote>(entity =>
        {
            entity.HasInMemoryStore<NoteSeed, int>(row => row.Key,
                    row => new NoteSeed(row.Key, row.Caption, row.Revision, row.Archived, row.Marker.ToArray(), row.Tag))
                // This initializer declares scalar slots; the generator never executes the entity constructor.
                .MapInMemoryEntity<NoteSeed>(row => new PrivateNote
                    { Id = row.Key, Title = row.Caption, Version = row.Revision, Hidden = row.Archived, Stamp = row.Marker })
                // A separate write adapter; no reverse mapping is inferred. saved is null for inserts.
                .MapInMemoryWrites<NoteSeed>((row, saved) => new NoteSeed(row.Id, row.Title, row.Version, row.Hidden,
                    row.Stamp.ToArray(), saved == null ? "created" : saved.Tag))
                .HasInMemoryGeneratedValue(row => row.Id, (row, rows) => checked(rows.Select(item => item.Id).DefaultIfEmpty().Max() + 1))
                .HasInMemoryGeneratedValue(row => row.Version, (row, rows) => row.Title.Length);
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasProjection(row => new NoteCard(row.Id, row.Title, row.Version, row.Stamp.ToArray()));
            entity.HasQuery("List").OrderBy(row => row.Id).ProjectTo<NoteCard>().List();
            foreach (var save in SaveModes)
            {
                var create = entity.HasCommand(save ? "Create" : "StageCreate").Create<NoteChange>(request => new PrivateNote
                    { Id = request.Id, Title = request.Title, Hidden = false, Stamp = request.Marker ?? Array.Empty<byte>() }).AllowWrite(row => row.Id);
                var rename = entity.HasCommand(save ? "Rename" : "StageRename").Where<NoteChange>((row, request) => row.Id == request.Id)
                    .Patch(setters => setters.SetProperty<string, NoteChange>(row => row.Title, (row, request) => request.Title)
                        .SetPropertyWhen<byte[], NoteChange>(row => row.Stamp, (row, request) => request.Marker!, request => request.Marker != null))
                    .ExpectVersion<int, NoteChange>(row => row.Version, (row, request) => request.ExpectedRevision);
                var remove = entity.HasCommand(save ? "Remove" : "StageRemove").Where<NoteChange>((row, request) => row.Id == request.Id)
                    .ExpectVersion<int, NoteChange>(row => row.Version, (row, request) => request.ExpectedRevision).TrackedDelete();
                if (save)
                {
                    create.Save().Returns(row => new NoteCard(row.Id, row.Title, row.Version, row.Stamp.ToArray()));
                    rename.Save().Returns(row => new NoteCard(row.Id, row.Title, row.Version, row.Stamp.ToArray()));
                    remove.Save();
                }
                else { create.Stage(); rename.Stage(); remove.Stage(); }
            }
            entity.HasCommand("RenameMany").PatchMany<NoteChange, int, IReadOnlyList<NoteChange>>(items => items, row => row.Id, item => item.Id,
                setters => setters.SetProperty(row => row.Title, (row, item) => item.Title).ExpectVersion(row => row.Version, (row, item) => item.ExpectedRevision), maximumItems: 4)
                .Save().Returns(row => new NoteCard(row.Id, row.Title, row.Version, row.Stamp.ToArray()));
        });
        modelBuilder.Entity<PrivateAudit>(entity =>
        {
            entity.ReadEnabled = false;
            entity.HasInMemoryStore<AuditSeed, int>(row => row.Key, row => new AuditSeed(row.Key, row.Caption))
                .MapInMemoryEntity<AuditSeed>(row => new PrivateAudit { Id = row.Key, Message = row.Caption })
                .MapInMemoryWrites<AuditSeed>((row, saved) => new AuditSeed(row.Id, row.Message));
            entity.HasCommand("StageAudit").Create<NoteChange>(request => new PrivateAudit { Id = request.Id, Message = request.Title })
                .AllowWrite(row => row.Id).Stage();
        });
    }
}
