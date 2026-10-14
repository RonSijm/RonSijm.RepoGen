// This example's application contracts; compiled by the shared EF-free Contracts project.
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public readonly record struct TaskId(int Value);

public readonly record struct ProjectId(Guid Value);

public sealed record TaskSummary(TaskId Id, ProjectId Project, string Title, string? Tag);

public sealed record CreateTask(ProjectId Project, string Title, string? Tag);

public sealed record RenameTask(TaskId Id, string Title);

public sealed record TaskBucket<TTag>(ProjectId Project, TTag Tag);

public interface ITaskDeskReads
{
    Task<TaskSummary?> FindAsync(TaskId taskId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskSummary>> ListByProjectAsync(ProjectId project, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<TaskBucket<string?>, IReadOnlyList<TaskSummary>>> ByBucketAsync(CancellationToken cancellationToken = default);
}

public interface ITaskDeskWrites
{
    Task<TaskSummary?> LoadForRenameAsync(TaskId taskId, CancellationToken cancellationToken = default);
    Task<CommandResult<TaskSummary>> CreateAsync(CreateTask request, CancellationToken cancellationToken = default);
    Task<CommandResult<TaskSummary>> RenameAsync(RenameTask request, CancellationToken cancellationToken = default);
}
