// Paired with ../Model/TaskDeskContext.cs by ../../GenerationRunner/Program.cs.
// API: ../../Contracts/Generated/TaskDeskRepositories.g.cs; implementation: ../Generated/Repositories.g.cs.
// See ../README.md for the declaration-to-output map; the consumer does not run this designer.
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.TaskDesk;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class TaskDeskRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.TaskDesk")
            .GenerateOnlyConfiguredEntities().IncludeApiDataContracts());
        var taskId = modelBuilder.HasTypeAdapter<int, TaskId>(value => new TaskId(value), value => value.Value);
        var projectId = modelBuilder.HasTypeAdapter<Guid, ProjectId>(value => new ProjectId(value), value => value.Value);
        var summary = modelBuilder.HasTypeAdapter<DeskTask, TaskSummary>(
            task => new TaskSummary(taskId.ToContract(task.Id), projectId.ToContract(task.ProjectId), task.Title, task.Tag),
            value => new DeskTask { Id = taskId.ToModel(value.Id), ProjectId = projectId.ToModel(value.Project), Title = value.Title, Tag = value.Tag });
        var creation = modelBuilder.HasTypeAdapter<DeskTask, CreateTask>(
            task => new CreateTask(projectId.ToContract(task.ProjectId), task.Title, task.Tag),
            request => new DeskTask { ProjectId = projectId.ToModel(request.Project), Title = request.Title, Tag = request.Tag });

        modelBuilder.Entity<DeskTask>(entity =>
        {
            entity.BindReadPort<ITaskDeskReads>();
            entity.BindWritePort<ITaskDeskWrites>();
            entity.HasAccessor(task => task.Id).UseAdapter(taskId);
            entity.HasAccessor(task => task.ProjectId).UseAdapter(projectId);
            entity.HasProjection(summary);
            entity.HasQuery("Find").Where<TaskId>((task, taskIdValue) => task.Id == taskId.ToModel(taskIdValue))
                .ProjectTo<TaskSummary>().SingleOrDefault();
            entity.HasQuery("LoadForRename").OnWriteRepository().Where<TaskId>((task, taskIdValue) => task.Id == taskId.ToModel(taskIdValue))
                .ProjectTo<TaskSummary>().SingleOrDefault();
            entity.HasQuery("ListByProject").Where<ProjectId>((task, project) => task.ProjectId == projectId.ToModel(project))
                .OrderBy(task => task.Id).ProjectTo<TaskSummary>().List();
            entity.HasQuery("ByBucket").Select(task => task)
                .Lookup(task => new TaskBucket<string?>(projectId.ToContract(task.ProjectId), task.Tag), task => summary.ToContract(task))
                .KeyNullability<TaskBucket<string>>(key => key.TypeArgument(0, tag => tag.Nullable()));
            entity.HasCommand("Create").Validate<CreateTask>(request => request.Title.Length > 0, "A title is required.")
                .Create<CreateTask>(request => creation.ToModel(request)).Save().Returns(task => summary.ToContract(task));
            entity.HasCommand("Rename").Where<RenameTask>((task, request) => task.Id == taskId.ToModel(request.Id))
                .Patch(setters => setters.SetProperty<string, RenameTask>(task => task.Title, (task, request) => request.Title))
                .Save().Returns(task => summary.ToContract(task));
        });
    }
}
