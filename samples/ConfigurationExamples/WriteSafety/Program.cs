using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.WriteSafety;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.WriteSafety;
using RonSijm.RepoGen.ConfigurationExamples.Models.WriteSafety;

// A runtime consumer only. Configuration lives in Configuration/AuditedNotesRepositoryDesigner.cs.
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var services = new ServiceCollection();
services.AddDbContext<WriteSafetyContext>(options => options.UseSqlite(connection));
services.AddWriteSafetyContextRepositories();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
await scope.ServiceProvider.GetRequiredService<WriteSafetyContext>().Database.EnsureCreatedAsync();
var writes = scope.ServiceProvider.GetRequiredService<IWriteAuditedNoteRepository>();
var created = await writes.CreateAsync(new CreateNoteRequest(7, "First title", "Original author"));
Require(created.Succeeded && created.Value is { Id: > 0, OwnerId: 7, CreatedBy: "Original author" }, "Creation uses explicit field permissions.");
var id = created.Value!.Id;
var renamed = await writes.RenameAsync(new RenameNoteRequest(id, 7, "Renamed", 1));
Require(renamed.Value is { Title: "Renamed", CreatedBy: "Original author", OwnerId: 7, Version: 2 }, "Renaming leaves protected attribution/ownership untouched.");
var wrongOwner = await writes.RenameAsync(new RenameNoteRequest(id, 8, "Wrong owner", 2));
Require(wrongOwner.Status == CommandStatus.NotFound, "Ownership belongs in the designed predicate.");
var corrected = await writes.CorrectAttributionAsync(new CorrectAttributionRequest(id, 7, "Corrected author", 2));
Require(corrected.Value is { CreatedBy: "Corrected author", Title: "Renamed", OwnerId: 7, Version: 3 }, "The separately permitted operation changes attribution only.");
var stale = await writes.CorrectAttributionAsync(new CorrectAttributionRequest(id, 7, "Stale correction", 2));
Require(stale.Status == CommandStatus.Conflict, "Stale corrections cannot overwrite the saved version.");
Console.WriteLine("WriteSafety: explicit audit/ownership guards, named permissions, saved DTOs and version conflicts passed.");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
