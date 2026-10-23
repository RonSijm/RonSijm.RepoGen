using System.Reflection;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class MappedStateTestHarness
{
    internal const string Namespace = "Generated.MappedState";
    private static readonly Lazy<Assembly> Generated = new(() =>
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder().Build()));
    });

    internal static MappedStateContext Context() => new(new DbContextOptionsBuilder<MappedStateContext>().UseSqlite("Data Source=:memory:").Options);

    internal static RepositoryModelBuilder Builder(bool state = true, bool queries = true, bool commands = true, bool strict = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries(queries)
            .UseInMemoryTestCommands(commands).RequirePortableTestCommands(commands && strict));
        var note = builder.Entity<MappedStateNote>();
        note.HasInMemoryStore<MappedStateSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Counter, row.Caption, row.Audit,
                row.Revision, row.OptionalVersion, row.Stamp == null ? null : row.Stamp.ToArray(), row.Extra))
            .MapInMemoryEntity<MappedStateSeed>(row => new MappedStateNote { Id = row.Id, Title = row.Title, Counter = row.Counter })
            .MapInMemoryProperty<MappedStateSeed, string>(nameof(MappedStateNote.Caption), row => row.Caption)
            .MapInMemoryProperty<MappedStateSeed, int>("audit", row => row.Audit)
            .MapInMemoryProperty<MappedStateSeed, int>("Revision", row => row.Revision)
            .MapInMemoryProperty<MappedStateSeed, int?>("OptionalVersion", row => row.OptionalVersion)
            .MapInMemoryProperty<MappedStateSeed, byte[]?>("Stamp", row => row.Stamp);
        WriteMap(note, state);
        note.HasInMemoryGeneratedValue<string>(nameof(MappedStateNote.Caption), (row, rows) => "caption")
            .HasInMemoryGeneratedValue<int>("audit", (row, rows) => 10)
            .HasInMemoryGeneratedValue<int?>("OptionalVersion", (row, rows) => 1)
            .HasInMemoryGeneratedValue<byte[]?>("Stamp", (row, rows) => new byte[] { 1, 2 })
            .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Counter + 1);
        note.HasVisibilityPolicy("Visible", row => row.Title != "hidden");
        note.HasProjection(Projection);
        note.HasQuery("Cards").OrderBy(row => row.Id).ProjectTo<MappedStateCard>().List();
        foreach (var save in new[] { false, true })
        {
            var rename = note.HasCommand(save ? "Rename" : "StageRename").Where<MappedStateRequest>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, MappedStateRequest>(row => row.Title, (row, request) => request.Title)
                    .SetProperty(row => row.Counter, row => row.Counter + 1))
                .ExpectVersion<int, MappedStateRequest>("Revision", request => request.Version);
            var remove = note.HasCommand(save ? "Remove" : "StageRemove").Where<MappedStateRequest>((row, request) => row.Id == request.Id)
                .TrackedDelete().ExpectVersion<int, MappedStateRequest>("Revision", request => request.Version);
            var create = note.HasCommand(save ? "Create" : "StageCreate").Create<MappedStateRequest>(request => new MappedStateNote
                { Id = request.Id, Title = request.Title, Counter = 0 }).AllowWrite(row => row.Id);
            if (save)
            {
                rename.Save().ReturnsSaved(Projection);
                remove.Save();
                create.Save().ReturnsSaved(Projection);
            }
            else
            {
                rename.Stage();
                remove.Stage();
                create.Stage();
            }
        }
        note.HasCommand("RenameMany").PatchMany<MappedStateRequest, int, IReadOnlyList<MappedStateRequest>>(items => items, row => row.Id, item => item.Id,
                setters => setters.SetProperty(row => row.Title, (row, item) => item.Title)
                    .SetProperty(row => row.Counter, row => row.Counter + 1)
                    .ExpectVersion<int>("Revision", item => item.Version), maximumItems: 4)
            .Save().ReturnsSaved(Projection);
        note.HasCommand("RemoveMany").DeleteMany<MappedStateRequest, int, IReadOnlyList<MappedStateRequest>>(items => items, row => row.Id, item => item.Id,
                versions => versions.ExpectVersion<int>("Revision", item => item.Version), maximumItems: 4).Save();
        note.HasCommand("AllTokens").Where<MappedStateRequest>((row, request) => row.Id == request.Id)
            .Patch(setters => setters.SetProperty<string, MappedStateRequest>(row => row.Title, (row, request) => request.Title))
            .ExpectVersion<int, MappedStateRequest>("Revision", request => request.Version)
            .ExpectVersion<int?, MappedStateRequest>("OptionalVersion", request => request.OptionalVersion)
            .ExpectVersion<byte[]?, MappedStateRequest>("Stamp", request => request.Stamp).Save();
        note.HasCommand("ExecuteRename").Where<MappedStateRequest>((row, request) => row.Id == request.Id)
            .Update(setters => setters.SetProperty<string, MappedStateRequest>(row => row.Title, (row, request) => request.Title)
                .SetProperty(row => row.Counter, row => row.Counter + 1))
            .ExpectVersion<int, MappedStateRequest>("Revision", request => request.Version).SimulateImmediateWrites();
        return builder;
    }

    internal static void WriteMap(RepositoryEntityBuilder<MappedStateNote> note, bool state = true)
        => note.MapInMemoryWrites<MappedStateSeed>((row, saved) => new(row.Id, row.Title, row.Counter, row.Caption,
            EF.Property<int>(row, "audit"), EF.Property<int>(row, "Revision"), EF.Property<int?>(row, "OptionalVersion"),
            (byte[]?)row["Stamp"], saved == null ? "created" : saved.Extra), useMappedPropertyState: state);

    private static Expression<Func<MappedStateNote, MappedStateCard>> Projection => row => new MappedStateCard(row.Id, row.Title, row.Counter, row.Caption,
        EF.Property<int>(row, "audit"), EF.Property<int>(row, "Revision"), EF.Property<int?>(row, "OptionalVersion"), (byte[]?)row["Stamp"]);

    internal static (object Repository, InMemoryWriteDataContext Data, InMemoryStore<MappedStateSeed, int> Store) Fixture(Assembly? assembly = null)
    {
        assembly ??= Generated.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MappedStateContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = (InMemoryStore<MappedStateSeed, int>)data.GetType().GetProperty("MappedStateNoteStore")!.GetValue(data)!;
        store.SeedRange(Seeds());
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MappedStateNoteRepositoryTestDouble")!, fixture)!, data, store);
    }

    internal static MappedStateSeed[] Seeds() =>
    [
        new(1, "old", 2, "caption", 10, 3, 1, [1, 2]),
        new(2, "other", 4, "caption", 10, 5, null, null),
        new(3, "hidden", 2, "caption", 10, 3, 1, [1, 2])
    ];

    internal static Task<TResult> Call<TResult>(object repository, string name, params object?[] inputs)
        => (Task<TResult>)repository.GetType().GetMethod(name)!.Invoke(repository, inputs.Append((object)CancellationToken.None).ToArray())!;
}
