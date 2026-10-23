using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicResults;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAtomicResults;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>A native insert/upsert returns a fresh DTO graph, not a native scalar receipt or a tracked graph.</summary>
public sealed class EnrollmentRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] Commands = ["Ensure", "Put"];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryAtomicResults")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryAtomicResults")
            .UseAtomicWrites<SqliteAtomicWriter>()
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryAtomicResults")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestCommands());
        modelBuilder.Entity<Course>(entity =>
        {
            // Keep the related dataset selected for detached result reads; it has no write operations.
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<CourseSeed, int>(row => row.Id, row => new(row.Id, row.Name))
                .MapInMemoryEntity<CourseSeed>(row => new Course { Id = row.Id, Name = row.Name });
            entity.HasInMemoryNavigation(row => row.Enrollments);
        });
        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.ReadEnabled = false;
            entity.BindWritePort<IEnrollmentWriter>();
            entity.HasInMemoryStore<EnrollmentSeed, int>(row => row.Id, row => new(row.Id, row.CourseId, row.Code, row.Name, row.Version))
                .MapInMemoryEntity<EnrollmentSeed>(row => new Enrollment
                    { Id = row.Id, CourseId = row.CourseId, Code = row.Code, Name = row.Name, Version = row.Version })
                .MapInMemoryWrites<EnrollmentSeed>((row, saved) => new(row.Id, row.CourseId, row.Code, row.Name, row.Version));
            entity.HasInMemoryNavigation(row => row.Course);
            foreach (var name in Commands)
            {
                var command = entity.HasCommand(name).Create<EnrollmentChange>(request => new Enrollment
                    { Id = request.Id, CourseId = request.CourseId, Code = request.Code, Name = request.Name, Version = 1 })
                    .AllowWrite(row => row.Id).AllowWrite(row => row.CourseId).AllowWrite(row => row.Version);
                if (name == "Ensure")
                {
                    command.InsertIfAbsent(row => new { row.CourseId, row.Code });
                }
                else
                {
                    command.InsertOrUpdate(row => new { row.CourseId, row.Code }, setters => setters
                            .SetProperty<string, EnrollmentChange>(row => row.Name, (row, request) => request.Name)
                            .SetProperty(row => row.Version, row => row.Version + 1))
                        .ExpectVersion<int, EnrollmentChange>(row => row.Version, (row, request) => request.ExpectedVersion);
                }
                command.ReturnsSaved(row => new EnrollmentReceipt(row.Id, row.Name, row.Version,
                        row.Course == null ? "missing" : row.Course.Name,
                        row.Course == null ? new List<string>() : row.Course.Enrollments.OrderBy(member => member.Code).Select(member => member.Name).ToList()))
                    .SimulateImmediateWrites();
            }
        });
    }
}
