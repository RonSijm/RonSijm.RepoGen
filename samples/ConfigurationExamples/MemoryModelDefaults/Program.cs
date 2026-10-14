using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelDefaults;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryModelDefaults;

await using var provider = new ServiceCollection().AddDefaultSettingsContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<DefaultSettingsContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<IDefaultSettingReader>();
var writer = scope.ServiceProvider.GetRequiredService<IDefaultSettingWriter>();
fixture.Data.StoredDefaultSettingStore.Seed(new(90)); fixture.Data.CaptureBaseline();
if ((await reader.RowsAsync()).Single().Credits != -1) throw new InvalidOperationException("Seeds must not invoke insert defaults.");
var generated = (await writer.CreateAsync(new(1))).Value;
if (generated != new DefaultSettingSeed(1, 5, true, DefaultSettingLevel.Standard, "model")) throw new InvalidOperationException("Model constants must replace only sentinels.");
var supplied = new DefaultSettingSeed(2, 0, false, DefaultSettingLevel.Basic, "");
if ((await writer.CreateAsync(supplied)).Value != supplied) throw new InvalidOperationException("Explicit zero, false, basic enum and empty text must survive.");
await writer.StageAsync(new(3));
if ((await reader.RowsAsync()).Count != 3) throw new InvalidOperationException("Staged inserts are not saved reads.");
var batch = await writer.CreateManyAsync([new(5), new(4)]);
if (!batch.Value!.Select(row => row.Id).SequenceEqual([5, 4]) || (await reader.RowsAsync()).Count != 6) throw new InvalidOperationException("Batches must preserve input order and share Save with the staged insert.");
await writer.ResetCreditsAsync(1);
if ((await reader.RowsAsync()).Single(row => row.Id == 1).Credits != -1) throw new InvalidOperationException("Insert defaults must not overwrite update values.");
await using (var transaction = await fixture.Data.BeginTransactionAsync())
{
    await writer.CreateAsync(new(6)); await transaction.RollbackAsync();
}
if ((await reader.RowsAsync()).Any(row => row.Id == 6)) throw new InvalidOperationException("Explicit rollback must undo defaulted insertion too.");
fixture.Data.RestoreBaseline();
if ((await reader.RowsAsync()).Single() != new DefaultSettingSeed(90)) throw new InvalidOperationException("Baseline restore must retain arranged seed values, not reapply defaults.");
Console.WriteLine("MemoryModelDefaults: optional model constants, custom sentinels, supplied zero/false/enum values, mapped Stage/Save, ordered batches, update isolation and baseline/rollback passed without EF/models.");
