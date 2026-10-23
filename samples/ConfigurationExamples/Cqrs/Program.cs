// Repository usage and executable assertions, not generator configuration.
// Start with Configuration/WorkboardRepositoryDesigner.cs and README.md.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.Workboard;

namespace RonSijm.RepoGen.ConfigurationExamples.Cqrs;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await using var context = new WorkboardContext(new DbContextOptionsBuilder<WorkboardContext>().UseSqlite("Data Source=:memory:").Options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("CREATE VIEW Workload AS SELECT Priority, COUNT(*) AS Count FROM WorkItems WHERE Completed = 0 GROUP BY Priority");
        context.Items.AddRange(new WorkItem { Id = 1, Title = "Review", Priority = 10 }, new WorkItem { Id = 2, Title = "Publish", Priority = 20 });
        context.Labels.Add(new WorkLabel { Name = "Optional CQRS" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var services = new ServiceCollection();
        services.AddSingleton(context); // Borrowed, single-context sample; real applications ordinarily register a scoped context.
        services.AddWorkboardContextSelectorRepositories();
        services.AddScoped<WorkboardService>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var read = scope.ServiceProvider.GetRequiredService<IReadWorkItemRepository>();
        var service = scope.ServiceProvider.GetRequiredService<WorkboardService>();
        var detached = await read.ById(1).AsEntityAsync();
        if (detached is null || context.ChangeTracker.Entries().Any())
        {
            throw new InvalidOperationException("Read port unexpectedly tracked an entity.");
        }

        detached.Title = "A detached edit is not persisted";
        if (!(await service.ListAsync()).Select(item => item.Title).SequenceEqual(["Review", "Publish"]))
        {
            throw new InvalidOperationException("Configured projection/sort failed.");
        }

        if (await service.CompleteAsync(1) != 2)
        {
            throw new InvalidOperationException("The two write repositories did not share their unit-of-work save.");
        }

        context.ChangeTracker.Clear();
        if ((await service.ListAsync()).Single().Id != 2)
        {
            throw new InvalidOperationException("Staged patch did not persist.");
        }

        if ((await scope.ServiceProvider.GetRequiredService<IReadWorkLabelRepository>().ListLabelsAsync()).Single() != "Optional CQRS")
        {
            throw new InvalidOperationException("Read-only entity query failed.");
        }

        var workload = await scope.ServiceProvider.GetRequiredService<IReadWorkSummaryRepository>().ListWorkloadAsync();
        if (workload.Count != 1 || workload[0].Priority != 20 || workload[0].Count != 1 || context.ChangeTracker.Entries().Any())
        {
            throw new InvalidOperationException("Keyless read-only source failed.");
        }

        if (await context.Events.CountAsync() != 1)
        {
            throw new InvalidOperationException("Cross-repository staged event was not saved.");
        }

        Console.WriteLine("CQRS: detached reads, named tracked command loads, read/write-only entities, keyless views and shared saves passed.");
    }
}

// Named operations keep query details in the designer, even when selector-style ports are selected.
internal sealed class WorkboardService(IReadWorkItemRepository reads, IWriteWorkItemRepository writes, IWriteWorkEventRepository events)
{
    public Task<IReadOnlyList<WorkItemSummary>> ListAsync(CancellationToken cancellationToken = default) => reads.ListOpenAsync(cancellationToken);

    public async Task<int> CompleteAsync(int id, CancellationToken cancellationToken = default)
    {
        // All loading/tracking choices are in the designer, not a service-side selector chain.
        var item = await writes.LoadForUpdateAsync(id, cancellationToken);
        if (item is null)
        {
            return 0;
        }

        if ((await writes.CompleteAsync(id, cancellationToken)).Status != CommandStatus.Staged)
        {
            return 0;
        }

        await events.AddAsync(new WorkEvent { Message = "Completed item " + id + ": " + item.Title }, cancellationToken);
        return await writes.SaveChangesAsync(cancellationToken);
    }
}
