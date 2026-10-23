// See ../README.md for this designer's exact contract, implementation, manifest and API snapshot outputs.
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.ShadowVersions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class VersionedNotesRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ShadowVersions")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.ShadowVersions.Persistence")
            .UseReturningWrites<SqliteReturningWriter>().IncludeApiDataContracts());
        modelBuilder.Entity<VersionedNote>(entity =>
        {
            entity.BindWritePort<IVersionedNoteWrites>();
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id)
                .Select(row => new NoteVersionView(row.Id, row.Title, EF.Property<int>(row, "Version"))).SingleOrDefault();
            entity.HasCommand("Rename").Where<NoteVersionChange>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, NoteVersionChange>(row => row.Title, (row, request) => request.Title)
                    .SetProperty(row => row.ChangeCounter, row => row.ChangeCounter + 1))
                .ExpectVersion<int, NoteVersionChange>("Version", request => request.ExpectedVersion).Save()
                .ReturnsSaved(row => new NoteVersionView(row.Id, row.Title, EF.Property<int>(row, "Version")));
            entity.HasCommand("RenameNative").Where<NoteVersionChange>((row, request) => row.Id == request.Id)
                .Update(setters => setters.SetProperty<string, NoteVersionChange>(row => row.Title, (row, request) => request.Title)
                    .SetProperty(row => row.ChangeCounter, row => row.ChangeCounter + 1))
                .ExpectVersion<int, NoteVersionChange>("Version", request => request.ExpectedVersion)
                .ReturnsMany(row => new NoteVersionView(row.Id, row.Title, EF.Property<int>(row, "Version"))).IncludeShadowProperties();
            entity.HasCommand("RenameMany").PatchMany<NoteVersionChange, int, NoteVersionBatch>(request => request.Items, row => row.Id, item => item.Id,
                    items => items.SetProperty(row => row.Title, (row, item) => item.Title)
                        .SetProperty(row => row.ChangeCounter, row => row.ChangeCounter + 1)
                        .ExpectVersion<int>("Version", item => item.ExpectedVersion), maximumItems: 10).Save()
                .ReturnsSaved(row => new NoteVersionView(row.Id, row.Title, EF.Property<int>(row, "Version")));
        });
    }
}
