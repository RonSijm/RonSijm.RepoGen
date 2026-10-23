using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryKeyIdentity;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryKeyIdentity;
using RonSijm.RepoGen.Testing;

var services = new ServiceCollection(); services.AddKeyDirectoryContextRepositoryTestDoubles();
await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<KeyDirectoryContextRepositoryFixture>();
var read = scope.ServiceProvider.GetRequiredService<IReadDictionaryEntryRepository>();
var write = scope.ServiceProvider.GetRequiredService<IWriteDictionaryEntryRepository>();
var store = fixture.Data.DictionaryEntryStore;
store.Seed(new("ABC", "first")); fixture.Data.CaptureBaseline();
Check(store.Find("abc")!.Caption == "first", "Fixture lookups use explicitly configured key identity.");
Check(await read.FindAsync("abc") is null && (await read.FindAsync("ABC"))!.Caption == "first", "Designer predicates retain ordinary CLR equality, not pretend database collation.");
store.Seed(new("abc", "ignored"), InMemoryDuplicateKeyBehavior.Ignore);
Check(store.Find("AbC")!.Caption == "first", "Arrangement duplicate handling uses the same comparer.");
store.Seed(new("abc", "replacement"), InMemoryDuplicateKeyBehavior.Replace);
Check(store.Count == 1 && store.Find("ABC")!.Caption == "replacement", "Replace updates one logical fixture key.");
fixture.Data.RestoreBaseline();
await write.StageCreateAsync(new("DEF", "pending"));
try
{
    await write.StageCreateAsync(new("def", "duplicate"));
    throw new InvalidOperationException("Equivalent pending fixture keys must be rejected.");
}
catch (InvalidOperationException exception) when (exception.Message.StartsWith("Duplicate key in in-memory fixture store:", StringComparison.Ordinal)) { }
Check(fixture.Data.PendingCount == 1 && store.Find("def") is null, "A duplicate failure retains the earlier staged item.");
Check(await fixture.Data.SaveChangesAsync() == 1 && store.Find("def")!.Caption == "pending", "Shared saves preserve configured identity in mapped public rows.");
await using (await fixture.Data.BeginTransactionAsync())
{
    await write.RenameAsync(new("ABC", "temporary")); await write.RemoveAsync("DEF");
}
Check(store.Find("abc")!.Caption == "first" && store.Find("def")!.Caption == "pending", "Checkpoint rollback restores comparer-backed stores.");
fixture.Data.RestoreBaseline();
Check(store.Count == 1 && store.Remove("aBc"), "Baseline reset and arrangement removals retain key identity.");
Console.WriteLine("MemoryKeyIdentity: opt-in fixture comparers, unchanged predicates, duplicate policies, Stage/Save, baseline and rollback passed without EF.");
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
