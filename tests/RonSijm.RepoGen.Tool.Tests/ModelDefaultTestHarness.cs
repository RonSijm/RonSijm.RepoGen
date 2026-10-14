using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class ModelDefaultTestHarness
{
    internal const string Namespace = "Generated.ModelDefaults";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    internal static ModelDefaultContext Context() => new(new DbContextOptionsBuilder<ModelDefaultContext>().UseSqlite("Data Source=:memory:").Options);
    private static Assembly Compile(bool mapped) { using var context = Context(); return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build())); }
    internal static RepositoryModelBuilder Builder(bool mapped = true, bool enabled = true, bool backend = true, bool strict = true, bool manual = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries()
            .UseInMemoryTestCommands(backend).RequirePortableTestCommands(backend && strict));
        var entity = builder.Entity<ModelDefaultRow>();
        if (mapped) entity.HasInMemoryStore<ModelDefaultData, int>(row => row.Id, row => new(row.Id, row.Credits, row.Enabled, row.Level, row.Label, row.Score, row.Priority))
            .MapInMemoryEntity<ModelDefaultData>(row => new ModelDefaultRow { Id = row.Id, Credits = row.Credits, Enabled = row.Enabled, Level = row.Level, Label = row.Label, Score = row.Score, Priority = row.Priority })
            .MapInMemoryWrites<ModelDefaultData>((row, saved) => new(row.Id, row.Credits, row.Enabled, row.Level, row.Label, row.Score, row.Priority));
        else entity.HasInMemoryStore(row => row.Id, row => new ModelDefaultRow { Id = row.Id, Credits = row.Credits, Enabled = row.Enabled, Level = row.Level, Label = row.Label, Score = row.Score, Priority = row.Priority });
        entity.UseInMemoryModelDefaults(enabled);
        if (manual)
        {
            entity.HasInMemoryGeneratedValue(row => row.Credits, (row, existing) => 9);
            entity.HasInMemoryGeneratedValue(row => row.Enabled, (row, existing) => true);
            entity.HasInMemoryGeneratedValue(row => row.Level, (row, existing) => ModelDefaultLevel.Standard);
            entity.HasInMemoryGeneratedValue(row => row.Label, (row, existing) => "model");
            entity.HasInMemoryGeneratedValue(row => row.Score, (row, existing) => 7);
            entity.HasInMemoryGeneratedValue(row => row.Priority, (row, existing) => (byte)10);
        }
        entity.HasQuery("Rows").OrderBy(row => row.Id).Select(row => new ModelDefaultData(row.Id, row.Credits, row.Enabled, row.Level, row.Label, row.Score, row.Priority)).List();
        foreach (var name in new[] { "Create", "StageCreate" })
        {
            var command = entity.HasCommand(name).Create<ModelDefaultData>(request => new ModelDefaultRow { Id = request.Id, Credits = request.Credits, Enabled = request.Enabled,
                Level = request.Level, Label = request.Label, Score = request.Score, Priority = request.Priority });
            Permit(command);
            if (name == "StageCreate") command.Stage(); else command.Save().ReturnsSaved(row => new ModelDefaultData(row.Id, row.Credits, row.Enabled, row.Level, row.Label, row.Score, row.Priority));
        }
        var batch = entity.HasCommand("CreateMany").CreateMany<ModelDefaultData, IReadOnlyList<ModelDefaultData>>(items => items,
            item => new ModelDefaultRow { Id = item.Id, Credits = item.Credits, Enabled = item.Enabled, Level = item.Level, Label = item.Label, Score = item.Score, Priority = item.Priority }, maximumItems: 4);
        Permit(batch); batch.Save().ReturnsSaved(row => new ModelDefaultData(row.Id, row.Credits, row.Enabled, row.Level, row.Label, row.Score, row.Priority));
        entity.HasCommand("Reset").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Credits, -1)).AllowWrite(row => row.Credits).Save();
        return builder;
    }
    private static void Permit(RepositoryCommandBuilder<ModelDefaultRow> command) => command.AllowWrite(row => row.Id).AllowWrite(row => row.Credits).AllowWrite(row => row.Enabled)
        .AllowWrite(row => row.Level).AllowWrite(row => row.Label).AllowWrite(row => row.Score).AllowWrite(row => row.Priority);
    internal static (object Repository, InMemoryWriteDataContext Data) Fixture(bool mapped, RepositoryModelBuilder? custom = null)
    {
        Assembly assembly;
        if (custom is null) assembly = mapped ? Mapped.Value : Entities.Value;
        else { using var context = Context(); assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, custom.Build())); }
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelDefaultContextRepositoryFixture")!)!;
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelDefaultRowRepositoryTestDouble")!, fixture)!,
            (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!);
    }
    internal static Task<T> Call<T>(object repository, string name, params object?[] inputs) => NavigationReadTestHarness.Invoke<Task<T>>(repository, name + "Async", inputs);
}
