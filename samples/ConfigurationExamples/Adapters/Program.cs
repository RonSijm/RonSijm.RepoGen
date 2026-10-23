// Repository usage and executable assertions, not generator configuration.
// Start with Configuration/TaskDeskRepositoryDesigner.cs and README.md.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.TaskDesk;
using RonSijm.RepoGen.ConfigurationExamples.Models.TaskDesk;

namespace RonSijm.RepoGen.ConfigurationExamples.Adapters;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<TaskDeskContext>(options => options.UseSqlite(connection));
        services.AddTaskDeskContextRepositories();
        services.AddScoped<TaskDeskService>();
        await using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaskDeskContext>();
        await context.Database.EnsureCreatedAsync();
        var project = new ProjectId(Guid.Parse("81958a74-5a71-411d-89da-f004f8af2b64"));
        var service = scope.ServiceProvider.GetRequiredService<TaskDeskService>();
        var first = await service.CreateAsync(new CreateTask(project, "First", null));
        var second = await service.CreateAsync(new CreateTask(project, "Second", null));
        if (!first.HasValue || !second.HasValue || first.Value!.Id.Value == second.Value!.Id.Value)
        {
            throw new InvalidOperationException("Generated database IDs were not adapted into application IDs.");
        }

        var renamed = await service.RenameAsync(new RenameTask(first.Value.Id, "Renamed"));
        if (renamed.Value!.Title != "Renamed" || (await service.FindAsync(first.Value.Id))!.Title != "Renamed")
        {
            throw new InvalidOperationException("Typed request/response mappings did not persist.");
        }

        if ((await service.LoadForRenameAsync(first.Value.Id))?.Title != "Renamed")
        {
            throw new InvalidOperationException("The EF-free write port did not expose its configured command-side read.");
        }

        var tasks = await service.ListAsync(project);
        var buckets = await service.BucketsAsync();
        if (tasks.Count != 2 || buckets[new TaskBucket<string?>(project, null)].Count != 2)
        {
            throw new InvalidOperationException("Typed project filtering or nullable-part dictionary keys failed.");
        }

        Console.WriteLine("Adapters: typed IDs, EF-free command reads, reusable mappings, saved generated values and nullable dictionary keys passed.");
    }
}

// Application service code depends only on EF-free ports, typed requests, IDs and results.
internal sealed class TaskDeskService(ITaskDeskReads reads, ITaskDeskWrites writes)
{
    public Task<CommandResult<TaskSummary>> CreateAsync(CreateTask request, CancellationToken cancellationToken = default) => writes.CreateAsync(request, cancellationToken);
    public Task<CommandResult<TaskSummary>> RenameAsync(RenameTask request, CancellationToken cancellationToken = default) => writes.RenameAsync(request, cancellationToken);
    public Task<TaskSummary?> LoadForRenameAsync(TaskId id, CancellationToken cancellationToken = default) => writes.LoadForRenameAsync(id, cancellationToken);
    public Task<TaskSummary?> FindAsync(TaskId id, CancellationToken cancellationToken = default) => reads.FindAsync(id, cancellationToken);
    public Task<IReadOnlyList<TaskSummary>> ListAsync(ProjectId project, CancellationToken cancellationToken = default) => reads.ListByProjectAsync(project, cancellationToken);
    public Task<IReadOnlyDictionary<TaskBucket<string?>, IReadOnlyList<TaskSummary>>> BucketsAsync(CancellationToken cancellationToken = default) => reads.ByBucketAsync(cancellationToken);
}
