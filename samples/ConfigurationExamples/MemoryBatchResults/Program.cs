using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryBatchResults;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryBatchResults;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryBatchResults;

var services = new ServiceCollection(); services.AddBatchNotebookContextRepositoryTestDoubles();
await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<BatchNotebookContextRepositoryFixture>();
var notebooks = scope.ServiceProvider.GetRequiredService<IWriteBatchNotebookRepository>();
var notes = scope.ServiceProvider.GetRequiredService<IWriteBatchNoteRepository>();
fixture.Data.BatchNotebookStore.SeedRange([new(1, 1, "a", "draft"), new(2, 1, "b", "draft")]);
fixture.Data.BatchNoteStore.SeedRange([new(1, 1, 1, "A", false), new(2, 2, 1, "B", false), new(3, 1, 1, "archived", true)]);
fixture.Data.CaptureBaseline();

await notes.StageTextAsync(1);
var result = await notebooks.RenameManyAsync(new("published", [new("b", "Second"), new("a", "First")]));
var values = result.Value ?? throw new InvalidOperationException("The saved batch must have a result.");
if (result.Status != CommandStatus.Applied || result.AffectedRows != 3 || !values.Select(row => row.Code).SequenceEqual(["b", "a"]))
    throw new InvalidOperationException("One shared save must return one fresh DTO per input, in input order.");
if (values[0].TenantId != 2 || values[1].TenantId != 1 || values[0].Name != "Second" || values[1].Name != "First")
    throw new InvalidOperationException("Complete composite saved keys must distinguish notebooks with the same local ID.");
if (!values[1].Notes.SequenceEqual(["shared save"]) || !values[0].Notes.SequenceEqual(["B"]) || values.Any(row => row.Label != "published"))
    throw new InvalidOperationException("The result must read saved related tables, explicit visibility and request values.");
((List<string>)values[1].Notes).Clear();
if (fixture.Data.BatchNoteStore.Count != 3) throw new InvalidOperationException("DTO collection changes must not mutate fixture data.");

fixture.Data.RestoreBaseline();
await using (await fixture.Data.BeginTransactionAsync())
{
    await notebooks.RenameManyAsync(new("temporary", [new("a", "Temporary")]));
}
if (fixture.Data.BatchNotebookStore.Snapshot().Any(row => row.Name != "draft"))
    throw new InvalidOperationException("An explicit checkpoint rollback must restore the saved batch.");
var missing = await notebooks.RenameManyAsync(new("missing", [new("a", "Ignored"), new("missing", "Ignored")]));
if (missing.Status != CommandStatus.NotFound || fixture.Data.PendingCount != 0)
    throw new InvalidOperationException("All targets are required before staging, saving or projecting any batch result.");
Console.WriteLine("MemoryBatchResults: ordered fresh nested DTOs, composite saved keys, shared save and rollback passed without EF.");
