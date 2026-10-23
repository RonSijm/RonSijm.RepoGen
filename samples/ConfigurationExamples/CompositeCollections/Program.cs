using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.CompositeCollections;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.CompositeCollections;

var services = new ServiceCollection(); services.AddTranslationCatalogContextRepositoryTestDoubles();
await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<TranslationCatalogContextRepositoryFixture>();
var write = scope.ServiceProvider.GetRequiredService<IWriteLocalizedDocumentRepository>();
var read = scope.ServiceProvider.GetRequiredService<IReadLocalizedTextRepository>();
fixture.Data.LocalizedDocumentStore.SeedRange([new(10, 1), new(20, 1)]);
fixture.Data.LocalizedTextStore.SeedRange([
    new(1, 10, 1, "title", "en", "old English"), new(2, 10, 1, "title", "nl", "old Dutch"),
    new(3, 10, 1, "summary", "en", "old summary"), new(4, 10, 1, "hidden", "en", "hidden", Hidden: true),
    new(5, 20, 1, "title", "en", "other tenant")]);
fixture.Data.CaptureBaseline();
Check((await write.StageTranslationsAsync(new(10, 1, [new("title", "en", "English"), new("title", "fr", "French")]))).Status == CommandStatus.Staged,
    "Matching is by the entire Code/Locale combination, not a child ID.");
Check((await read.ListTranslationsAsync(new(10, 1))).Count == 3, "Staging does not change saved reads.");
Check(await fixture.Data.SaveChangesAsync() == 5, "The parent, updates, addition and missing deletions share one save.");
var texts = await read.ListTranslationsAsync(new(10, 1));
Check(texts.Select(row => row.Locale).SequenceEqual(["en", "fr"]), "Equal codes in different locales are distinct keys.");
Check(fixture.Data.LocalizedTextStore.Find(6) is { TenantId: 10, DocumentId: 1, Code: "title", Locale: "fr" }, "Every ownership component comes from the selected parent.");
Check(fixture.Data.LocalizedTextStore.Find(4)!.Hidden && fixture.Data.LocalizedTextStore.Find(5)!.Text == "other tenant", "Hidden rows and the same key under another owner are untouched.");
var conflict = await write.SaveTranslationsAsync(new(10, 1, [new("title", "en", "stale", 99)], ExpectedVersion: 2));
Check(conflict.Status == CommandStatus.Conflict && fixture.Data.PendingCount == 0, "Stale child versions restore the entire command.");
try { await write.SaveTranslationsAsync(new(10, 1, [new("title", null!, "invalid")], ExpectedVersion: 2)); throw new InvalidOperationException("Expected null-key rejection."); }
catch (ArgumentException) { Check(fixture.Data.PendingCount == 0, "A null tuple component fails before staging."); }
await using (await fixture.Data.BeginTransactionAsync())
{
    Check((await write.SaveTranslationsAsync(new(10, 1, [], ExpectedVersion: 2))).Value == 3, "Empty input is an explicit visible replacement.");
    Check((await read.ListTranslationsAsync(new(10, 1))).Count == 0, "Explicit deletion is saved inside the checkpoint.");
}
Check((await read.ListTranslationsAsync(new(10, 1))).Count == 2, "Rollback restores all participating fixture stores.");
fixture.Data.RestoreBaseline();
Console.WriteLine("CompositeCollections: designed composite matching, complete ownership, public rows, versions, Stage/Save, null-key rejection and rollback passed without EF.");
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
