using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class MappedAtomicTestHarness
{
    internal const string Namespace = "Generated.MappedAtomic";
    private static readonly Lazy<Assembly> Generated = new(() => Compile(Builder()));

    internal static MappedAtomicContext Context(bool sqlServer = false)
    {
        var options = new DbContextOptionsBuilder<MappedAtomicContext>();
        if (sqlServer) options.UseSqlServer("Server=unused;Database=unused");
        else options.UseSqlite("Data Source=:memory:");
        return new(options.Options);
    }

    internal static Expression<Func<MappedStateNote, MappedStateCard>> Projection => row => new MappedStateCard(row.Id,
        row.Title, row.Counter, row.Caption, EF.Property<int>(row, "audit"), EF.Property<int>(row, "Revision"),
        EF.Property<int?>(row, "OptionalVersion"), EF.Property<byte[]?>(row, "Stamp"));

    internal static RepositoryModelBuilder Builder(bool simulate = true, bool state = true, bool strict = true,
        bool queries = true, bool commands = true, bool filter = true, bool sqlServer = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries(queries)
            .UseInMemoryTestCommands(commands).RequirePortableTestCommands(commands && strict));
        if (sqlServer) builder.Configure(options => options.UseAtomicWrites<RonSijm.RepoGen.SqlServer.SqlServerAtomicWriter>());
        else builder.Configure(options => options.UseAtomicWrites<SqliteAtomicWriter>());
        var entity = builder.Entity<MappedStateNote>();
        entity.HasInMemoryStore<MappedStateSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Counter, row.Caption,
                row.Audit, row.Revision, row.OptionalVersion, row.Stamp == null ? null : row.Stamp.ToArray(), row.Extra))
            .MapInMemoryEntity<MappedStateSeed>(row => new MappedStateNote { Id = row.Id, Title = row.Title, Counter = row.Counter })
            .MapInMemoryProperty<MappedStateSeed, string>(nameof(MappedStateNote.Caption), row => row.Caption)
            .MapInMemoryProperty<MappedStateSeed, int>("audit", row => row.Audit)
            .MapInMemoryProperty<MappedStateSeed, int>("Revision", row => row.Revision)
            .MapInMemoryProperty<MappedStateSeed, int?>("OptionalVersion", row => row.OptionalVersion)
            .MapInMemoryProperty<MappedStateSeed, byte[]?>("Stamp", row => row.Stamp)
            .MapInMemoryWrites<MappedStateSeed>((row, saved) => new(row.Id, row.Title, row.Counter, row.Caption,
                EF.Property<int>(row, "audit"), EF.Property<int>(row, "Revision"), EF.Property<int?>(row, "OptionalVersion"),
                EF.Property<byte[]?>(row, "Stamp"), saved == null ? "created" : saved.Extra), useMappedPropertyState: state)
            .HasInMemoryGeneratedValue<string>(nameof(MappedStateNote.Caption), (row, rows) => row.Title.ToUpperInvariant())
            .HasInMemoryGeneratedValue<int>("audit", (row, rows) => row.Counter + 10)
            .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Counter + 1)
            .HasInMemoryGeneratedValue<int?>("OptionalVersion", (row, rows) => 1)
            .HasInMemoryGeneratedValue<byte[]?>("Stamp", (row, rows) => new byte[] { 1, 2 });
        entity.HasVisibilityPolicy("Visible", row => row.Title != "hidden");
        entity.UseInMemoryModelQueryFilter();
        entity.HasQuery("Cards").OrderBy(row => row.Id).Select(Projection).List();
        foreach (var name in new[] { "Ensure", "Put", "FailEnsure", "FailPut" })
        {
            var command = entity.HasCommand(name).Validate<MappedAtomicRequest>(request => request.Title != "", "Title required.")
                .Create<MappedAtomicRequest>(request => new MappedStateNote { Id = request.Id, Title = request.Title, Counter = request.Delta })
                .AllowWrite(row => row.Id);
            if (name.EndsWith("Put", StringComparison.Ordinal))
                command.Where<MappedAtomicRequest>((row, request) => row.Counter >= request.Minimum)
                    .InsertOrUpdate(row => row.Title, setters => setters.SetProperty<int, MappedAtomicRequest>(row => row.Counter, (row, request) => row.Counter + request.Delta))
                    .ExpectVersion<int, MappedAtomicRequest>("Revision", request => request.Revision)
                    .ExpectVersion<int?, MappedAtomicRequest>("OptionalVersion", request => request.OptionalVersion)
                    .ExpectVersion<byte[]?, MappedAtomicRequest>("Stamp", request => request.Stamp);
            else command.InsertIfAbsent(row => row.Title);
            if (name.StartsWith("Fail", StringComparison.Ordinal)) command.Returns(row => new MappedAtomicFailure(EF.Property<int>(row, "Revision")));
            else command.Returns(Projection);
            command.IncludeMappedProperties().SimulateImmediateWrites(simulate).UseInMemoryModelQueryFilter(filter);
        }
        entity.HasCommand("StageCounter").Where<MappedAtomicRequest>((row, request) => row.Id == request.Id)
            .Patch(setters => setters.SetProperty<int, MappedAtomicRequest>(row => row.Counter, (row, request) => row.Counter + request.Delta))
            .ExpectVersion<int, MappedAtomicRequest>("Revision", request => request.Revision)
            .UseInMemoryModelQueryFilter().Stage();
        return builder;
    }

    internal static Assembly Compile(RepositoryModelBuilder builder)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    internal static (object Repository, InMemoryWriteDataContext Data, InMemoryStore<MappedStateSeed, int> Store) Fixture(Assembly? assembly = null)
    {
        assembly ??= Generated.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MappedAtomicContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = (InMemoryStore<MappedStateSeed, int>)data.GetType().GetProperty("MappedStateNoteStore")!.GetValue(data)!;
        store.SeedRange(Seeds());
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MappedStateNoteRepositoryTestDouble")!, fixture)!, data, store);
    }

    internal static MappedStateSeed[] Seeds() =>
    [
        new(1, "one", 2, "ONE", 12, 3, 1, [1, 2]),
        new(2, "other", 4, "OTHER", 14, 5, null, null),
        new(3, "hidden", 2, "HIDDEN", 12, 3, 1, [1, 2]),
        new(4, "filtered", 10, "FILTERED", 20, 11, 1, [1, 2])
    ];

    internal static Task<TResult> Call<TResult>(object repository, string operation, MappedAtomicRequest request, CancellationToken token = default)
        => (Task<TResult>)repository.GetType().GetMethod(operation + "Async")!.Invoke(repository, [request, token])!;

    internal static async Task SeedAsync(MappedAtomicContext context)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.AddRange(Seeds().Select(seed => new MappedStateNote { Id = seed.Id, Title = seed.Title, Counter = seed.Counter }));
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("UPDATE \"MappedStateNote\" SET \"OptionalVersion\" = NULL, \"Stamp\" = NULL WHERE \"Id\" = 2");
        context.ChangeTracker.Clear();
    }
}
