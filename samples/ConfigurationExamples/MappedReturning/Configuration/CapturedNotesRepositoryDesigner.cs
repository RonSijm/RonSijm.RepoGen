// See ../README.md for the exact designer → contract/implementation/manifest/API snapshot map.
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.MappedReturning;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class CapturedNotesRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MappedReturning")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.MappedReturning.Persistence")
            .UseReturningWrites<SqliteReturningWriter>().IncludeApiDataContracts());
        modelBuilder.Entity<CapturedNote>(entity =>
        {
            entity.ReadEnabled = false;
            entity.BindWritePort<ICapturedNoteWrites>();
            var rename = entity.HasCommand("Rename").Where<CapturedNoteRequest>((row, request) => row.Id == request.Id)
                .Update(setters => setters.SetProperty<string, CapturedNoteRequest>(row => row.Title, (row, request) => request.Title)
                    .SetProperty(row => row.ChangeCounter, row => row.ChangeCounter + 1));
            var remove = entity.HasCommand("Remove").Where<CapturedNoteRequest>((row, request) => row.Id == request.Id).Delete();
            foreach (var command in new[] { rename, remove })
            {
                command.ExpectVersion<int, CapturedNoteRequest>("version", request => request.ExpectedVersion)
                    .ReturnsMany(row => new CapturedNoteChange(row.Id, row.Title, row.NormalizedTitle,
                        EF.Property<int>(row, "version"), EF.Property<string?>(row, "SourceTag")))
                    .IncludeMappedProperties();
            }
        });
    }
}
