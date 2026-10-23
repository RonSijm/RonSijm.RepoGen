// Paired with AuditDocumentsContext in GenerationRunner/Program.cs; see ../README.md for exact outputs.
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.ShadowReturning;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class AuditDocumentsRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        // Direct context, no tenancy/cache/retry/fixture infrastructure. Native returning is opt-in.
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ShadowReturning")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.ShadowReturning.Persistence")
            .UseReturningWrites<SqliteReturningWriter>().IncludeApiDataContracts());
        modelBuilder.Entity<AuditDocument>(entity =>
        {
            entity.BindWritePort<IAuditDocumentWrites>();
            var rename = entity.HasCommand("Rename").Where<AuditDocumentRequest>((row, request) => row.Id == request.Id)
                .Update(setters => setters.SetProperty<string, AuditDocumentRequest>(row => row.Title, (row, request) => request.Title)
                    .SetProperty(row => row.Revision, row => row.Revision + 1)).AllowWrite(row => row.Revision);
            var remove = entity.HasCommand("Remove").Where<AuditDocumentRequest>((row, request) => row.Id == request.Id).Delete();
            foreach (var command in new[] { rename, remove })
                command.ExpectVersion<int, AuditDocumentRequest>(row => row.Revision, (row, request) => request.ExpectedRevision)
                    .ReturnsMany(row => new AuditDocumentChange(row.Id, row.Title, row.Revision,
                        EF.Property<string>(row, "NormalizedTitle"), EF.Property<int>(row, "TitleLength"), EF.Property<string?>(row, "SourceTag")))
                    .IncludeShadowProperties(); // Per command, not permission to emulate shadow state in fixtures.
        });
    }
}
