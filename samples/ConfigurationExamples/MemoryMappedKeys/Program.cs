using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedKeys;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryMappedKeys;
using RonSijm.RepoGen.Testing;

await using var provider = new ServiceCollection().AddKeyedNotebookContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<KeyedNotebookContextRepositoryFixture>();
var folders = scope.ServiceProvider.GetRequiredService<IKeyedFolderReader>();
var memos = scope.ServiceProvider.GetRequiredService<IKeyedMemoReader>();
fixture.Data.KeyedFolderStore.SeedRange([new(11, "alpha", "First"), new(22, "beta", "Second")]);
fixture.Data.KeyedMemoStore.SeedRange([new(1, "alpha", "A"), new(2, "beta", "B"), new(3, null, "Unfiled")]);
fixture.Data.CaptureBaseline();
var folderCards = await folders.CardsAsync();
if (!folderCards[0].Memos.SequenceEqual(["A"]) || !folderCards[1].Memos.SequenceEqual(["B"]))
{
    throw new InvalidOperationException("Explicit field/shadow keys must load each collection without calling the entity display getter.");
}
if (!(await memos.CardsAsync()).Select(row => row.Folder).SequenceEqual(["First", "Second", "none"]))
{
    throw new InvalidOperationException("The inverse reference needs its own mapped-key permission; null foreign keys never join.");
}
folderCards[0].Memos.Clear();
fixture.Data.KeyedFolderStore.Seed(new(11, "changed", "First"), InMemoryDuplicateKeyBehavior.Replace);
if ((await memos.CardsAsync())[0].Folder != "none" || fixture.Data.KeyedFolderStore.Find(11)!.Code != "changed")
{
    throw new InvalidOperationException("Relationship identity follows the declared alternate key, not the fixture dictionary's integer Id.");
}
fixture.Data.RestoreBaseline();
if ((await folders.CardsAsync())[0].Memos.Single() != "A")
{
    throw new InvalidOperationException("DTO mutation and detached relationship loading must not alter fixture arrangement or baselines.");
}
Console.WriteLine("MemoryMappedKeys: explicit field-backed principal/shadow foreign keys, both directions and isolated baselines passed without EF/models.");
