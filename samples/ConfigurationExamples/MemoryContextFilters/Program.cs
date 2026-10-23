using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryContextFilters;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryContextFilters;

await using var provider = new ServiceCollection().AddTenantNotebookContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<TenantNotebookContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<ITenantNoteReader>();
var writer = scope.ServiceProvider.GetRequiredService<ITenantNoteWriter>();
var first = new TenantPartition("A"); var second = new TenantPartition("B");
fixture.For(first).TenantNoteStore.SeedRange([new(1, "A", "first"), new(2, "B", "wrong tenant"), new(3, "A", "deleted", Deleted: true)]);
fixture.For(second).TenantNoteStore.SeedRange([new(1, "B", "second"), new(2, "A", "wrong tenant")]);
if ((await reader.CardsAsync(new(first))).Single().Title != "first" || (await reader.CardsAsync(new(second))).Single().Title != "second")
    throw new InvalidOperationException("Both partition selection and context-valued model filtering must apply.");
if ((await writer.RenameAsync(new(first, 2, "blocked"))).Status != CommandStatus.NotFound)
    throw new InvalidOperationException("The context-bound filter must also constrain tracked writes.");
if ((await writer.RenameAsync(new(first, 1, "renamed"))).Value?.Title != "renamed")
    throw new InvalidOperationException("Fresh saved DTOs must reuse the same key binding.");
await writer.StageRenameAsync(new(first, 1, "pending"));
if ((await writer.ArchiveAsync(new(first, 1))).AffectedRows != 1 || fixture.For(first).PendingCount != 1 || (await reader.CardsAsync(new(first))).Count != 0)
    throw new InvalidOperationException("Immediate filtered writes retain staged state and change fresh read visibility.");
await fixture.For(first).SaveChangesAsync();
if ((await reader.UnfilteredAsync(new(first))).Single(note => note.Id == 1).Title != "pending")
    throw new InvalidOperationException("Shared Save does not recheck the model filter or discard previously staged changes.");
if (fixture.For(second).PendingCount != 0 || (await reader.CardsAsync(new(second))).Single().Title != "second")
    throw new InvalidOperationException("Another partition must remain unchanged.");
Console.WriteLine("MemoryContextFilters: typed context-member binding, filtered reads/writes, fresh DTOs, pending state and isolated partitions passed without EF/models.");
