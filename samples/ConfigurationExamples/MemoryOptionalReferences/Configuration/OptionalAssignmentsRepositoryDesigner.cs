using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryOptionalReferences;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryOptionalReferences;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>One optional composite reference, explicit absence and immutable matches; no graph writes.</summary>
public sealed class OptionalAssignmentsRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] Operations = ["Replace", "Stage"];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryOptionalReferences")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryOptionalReferences")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryOptionalReferences")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.Entity<StoredWorkspace>(entity =>
        {
            entity.BindReadPort<IAssignmentReader>().BindWritePort<IAssignmentWriter>();
            entity.HasInMemoryStore<WorkspaceSeed, int>(row => row.Id, row => new(row.Id, row.Version))
                .MapInMemoryEntity<WorkspaceSeed>(row => new StoredWorkspace { Id = row.Id, Version = row.Version })
                .MapInMemoryWrites<WorkspaceSeed>((row, saved) => new(row.Id, row.Version));
            entity.HasInMemoryNavigation(row => row.Assignments);
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id).Select(row => new WorkspaceView(row.Id, row.Version,
                row.Assignments.OrderBy(link => link.SubjectId).Select(link => new AssignmentView(link.SubjectId,
                    link.OriginPartition, link.OriginCode, link.Caption, link.Version)).ToList())).SingleOrDefault();
            foreach (var operation in Operations)
            {
                var command = entity.HasCommand(operation).Where<ChangeAssignments>((row, request) => row.Id == request.WorkspaceId)
                    .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                    .ExpectVersion<int, ChangeAssignments>(row => row.Version, (row, request) => request.ExpectedVersion)
                    .ReconcileLinks<StoredAssignment, AssignmentInput, int, ChangeAssignments>(row => row.Assignments, request => request.Items,
                        row => row.SubjectId, item => item.SubjectId, links => links.TargetReference(row => row.Subject)
                            .OptionalAdditionalReference(row => row.Origin).MaximumItems(10)
                            .Add(item => new StoredAssignment { SubjectId = item.SubjectId, OriginPartition = item.OriginPartition,
                                OriginCode = item.OriginCode, Caption = item.Caption, Version = 1 })
                            .Update(setters => setters.SetProperty(row => row.Caption, (row, item) => item.Caption).SetProperty(row => row.Version, row => row.Version + 1))
                            .AllowWrite(row => row.Version).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                            .MissingItems(RepositoryCollectionRemovalBehavior.Delete)).SimulateCollectionWrites();
                if (operation == "Stage") command.Stage();
                else command.Save().ReturnsSaved(row => new WorkspaceView(row.Id, row.Version,
                    row.Assignments.OrderBy(link => link.SubjectId).Select(link => new AssignmentView(link.SubjectId,
                        link.OriginPartition, link.OriginCode, link.Caption, link.Version)).ToList()));
            }
        });
        modelBuilder.Entity<StoredSubject>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<SubjectSeed, int>(row => row.Id, row => new(row.Id))
                .MapInMemoryEntity<SubjectSeed>(row => new StoredSubject { Id = row.Id });
        });
        modelBuilder.Entity<StoredOrigin>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<OriginSeed, int>(row => row.Id, row => new(row.Id, row.Partition, row.Code))
                .MapInMemoryEntity<OriginSeed>(row => new StoredOrigin { Id = row.Id, Partition = row.Partition, Code = row.Code });
        });
        modelBuilder.Entity<StoredAssignment>(entity => entity
            .HasInMemoryStore<AssignmentSeed, (int, int)>(row => new ValueTuple<int, int>(row.WorkspaceId, row.SubjectId),
                row => new(row.WorkspaceId, row.SubjectId, row.OriginPartition, row.OriginCode, row.Caption, row.Version))
            .MapInMemoryEntity<AssignmentSeed>(row => new StoredAssignment { WorkspaceId = row.WorkspaceId, SubjectId = row.SubjectId,
                OriginPartition = row.OriginPartition, OriginCode = row.OriginCode, Caption = row.Caption, Version = row.Version })
            .MapInMemoryWrites<AssignmentSeed>((row, saved) => new(row.WorkspaceId, row.SubjectId, row.OriginPartition, row.OriginCode, row.Caption, row.Version)));
    }
}
