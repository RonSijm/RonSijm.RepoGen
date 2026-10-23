using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class ScalarEqualityTestHarness
{
    internal const string Namespace = "Generated.ScalarEquality";
    private static readonly Lazy<Assembly> Entities = new(() => Compile(Builder()));
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(Builder(mapped: true)));
    internal static ScalarEqualityContext Context() => new(new DbContextOptionsBuilder<ScalarEqualityContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder Builder(bool mapped = false, bool policies = true, bool commands = true, bool strict = true, bool keyed = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands(commands)
            .RequirePortableTestQueries().RequirePortableTestCommands(commands && strict));
        if (keyed) builder.Configure(options => options.UseKeyedContextLeases<string>());
        var entity = builder.Entity<ScalarEqualityRow>();
        if (mapped) entity.HasInMemoryStore<ScalarEqualitySeed, int>(row => row.Id, row => new(row.Id, row.Label, row.Revision, row.Amount, row.Hidden))
            .MapInMemoryEntity<ScalarEqualitySeed>(row => new ScalarEqualityRow { Id = row.Id, Label = row.Label, Revision = row.Revision, Amount = row.Amount, Hidden = row.Hidden })
            .MapInMemoryWrites<ScalarEqualitySeed>((row, saved) => new(row.Id, row.Label, row.Revision, row.Amount, row.Hidden));
        else entity.HasInMemoryStore(row => row.Id, row => new ScalarEqualityRow { Id = row.Id, Label = row.Label, Revision = row.Revision, Amount = row.Amount, Hidden = row.Hidden });
        if (policies) Policies(entity);
        entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
        if (!keyed)
        {
            entity.HasQuery("List").OrderBy(row => row.Id).Select(row => new ScalarEqualitySeed(row.Id, row.Label, row.Revision, row.Amount, row.Hidden)).List();
            entity.HasQuery("ByLabel").Where<string>((row, label) => row.Label == label).Select(row => row.Id).List();
        }
        foreach (var name in new[] { "SetLabel", "StageLabel", "Rename" })
        {
            var command = entity.HasCommand(name).Where<ScalarEqualityRequest>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string?, ScalarEqualityRequest>(row => row.Label, (row, request) => request.Label));
            if (name == "Rename") command.ExpectVersion<string, ScalarEqualityRequest>(row => row.Revision, (row, request) => request.ExpectedRevision)
                .Patch(setters => setters.SetProperty<string, ScalarEqualityRequest>(row => row.Revision, (row, request) => request.Revision)).AllowWrite(row => row.Revision);
            if (keyed) command.UseContextKey<ScalarEqualityRequest, string>(request => request.Partition);
            if (name == "StageLabel") command.Stage();
            else command.Save().ReturnsSaved(row => new ScalarEqualitySeed(row.Id, row.Label, row.Revision, row.Amount, row.Hidden));
        }
        var delete = entity.HasCommand("Delete").Where<ScalarEqualityRequest>((row, request) => row.Id == request.Id)
            .TrackedDelete().ExpectVersion<string, ScalarEqualityRequest>(row => row.Revision, (row, request) => request.ExpectedRevision)
            .Save().Returns(row => row.Id);
        if (keyed) delete.UseContextKey<ScalarEqualityRequest, string>(request => request.Partition);
        if (keyed) return builder;
        foreach (var name in new[] { "Mixed", "StageMixed", "ForceMixed", "StageForce" })
        {
            var mixed = entity.HasCommand(name).Where<ScalarEqualityRequest>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string?, ScalarEqualityRequest>(row => row.Label, (row, request) => request.Label)
                    .SetProperty<int, ScalarEqualityRequest>(row => row.Amount, (row, request) => request.Amount));
            if (name is "ForceMixed" or "StageForce") mixed.ExpectVersion<string, ScalarEqualityRequest>(row => row.Revision, (row, request) => request.ExpectedRevision);
            if (name.StartsWith("Stage", StringComparison.Ordinal)) mixed.Stage();
            else mixed.Save().ReturnsSaved(row => new ScalarEqualitySeed(row.Id, row.Label, row.Revision, row.Amount, row.Hidden));
        }
        entity.HasCommand("MixedMany").PatchMany<ScalarEqualityRequest, int, IReadOnlyList<ScalarEqualityRequest>>(items => items, row => row.Id, item => item.Id,
            setters => setters.SetProperty(row => row.Label, (row, item) => item.Label).SetProperty(row => row.Amount, (row, item) => item.Amount), maximumItems: 3)
            .Save().ReturnsSaved(row => new ScalarEqualitySeed(row.Id, row.Label, row.Revision, row.Amount, row.Hidden));
        entity.HasCommand("RenameMany").PatchMany<ScalarEqualityRequest, int, IReadOnlyList<ScalarEqualityRequest>>(items => items, row => row.Id, item => item.Id,
            setters => setters.SetProperty(row => row.Label, (row, item) => item.Label).SetProperty(row => row.Revision, (row, item) => item.Revision)
                .ExpectVersion(row => row.Revision, (row, item) => item.ExpectedRevision), maximumItems: 3).AllowWrite(row => row.Revision)
            .Save().ReturnsSaved(row => new ScalarEqualitySeed(row.Id, row.Label, row.Revision, row.Amount, row.Hidden));
        return builder;
    }
    internal static void Policies(RepositoryEntityBuilder<ScalarEqualityRow> entity)
    {
        entity.HasInMemoryValueEquality(row => row.Label, (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase));
        entity.HasInMemoryValueEquality(row => row.Revision, (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase));
    }
    internal static Assembly Compile(RepositoryModelBuilder builder)
    {
        using var context = Context(); return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }
    internal static (object Repository, InMemoryWriteDataContext Data) Fixture(bool mapped = false, Assembly? assembly = null, string? key = null)
    {
        assembly ??= mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ScalarEqualityContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ScalarEqualityRowRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryWriteDataContext)(key is null ? fixture.GetType().GetProperty("Data")!.GetValue(fixture)! : fixture.GetType().GetMethod("For")!.Invoke(fixture, [key])!);
        if (mapped) Store<ScalarEqualitySeed>(data).SeedRange(Seeds().Select(row => new ScalarEqualitySeed(row.Id, row.Label, row.Revision, row.Amount, row.Hidden)));
        else Store<ScalarEqualityRow>(data).SeedRange(Seeds());
        return (repository, data);
    }
    internal static InMemoryStore<T, int> Store<T>(InMemoryWriteDataContext data) where T : class => ManyReadTestHarness.Store<T, int>(data, nameof(ScalarEqualityRow));
    internal static ScalarEqualityRow[] Seeds() => [new() { Id = 1, Label = "Alpha" }, new() { Id = 2, Label = "Beta" }, new() { Id = 3, Label = null }, new() { Id = 4, Label = "Hidden", Hidden = true }];
    internal static ScalarEqualitySeed[] Rows(InMemoryWriteDataContext data, bool mapped)
        => (mapped ? Store<ScalarEqualitySeed>(data).Snapshot() : Store<ScalarEqualityRow>(data).Snapshot().Select(row => new ScalarEqualitySeed(row.Id, row.Label, row.Revision, row.Amount, row.Hidden))).OrderBy(row => row.Id).ToArray();
}
