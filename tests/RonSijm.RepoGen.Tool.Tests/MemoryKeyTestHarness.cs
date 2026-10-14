using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class MemoryKeyTestHarness
{
    internal static MemoryKeyContext Context() => new(new DbContextOptionsBuilder<MemoryKeyContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder Builder(bool keyed = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.MemoryKeys.Contracts")
            .UseGeneratedNamespace("Generated.MemoryKeys.Persistence").UseScriptedTestDoubles("Generated.MemoryKeys.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        if (keyed) builder.Configure(options => options.UseKeyedContextLeases<string>());
        var entity = builder.Entity<MemoryKeyRow>();
        entity.HasInMemoryStore<MemoryKeyData, string>(row => row.Key, row => new(row.Key, row.Caption, row.Revision, row.Extra))
            .MapInMemoryEntity<MemoryKeyData>(row => new MemoryKeyRow { Code = row.Key, Title = row.Caption, Version = row.Revision })
            .MapInMemoryWrites<MemoryKeyData>((row, saved) => new(row.Code, row.Title, row.Version, saved == null ? "created" : saved.Extra));
        var query = entity.HasQuery("Find").Where<MemoryKeyRequest>((row, request) => row.Code == request.Key);
        if (keyed) query.UseContextKey<MemoryKeyRequest, string>(request => request.Context);
        query.Select(row => new MemoryKeyData(row.Code, row.Title, row.Version, "query")).SingleOrDefault();
        foreach (var save in new[] { false, true })
        {
            var create = entity.HasCommand(save ? "Create" : "StageCreate").Create<MemoryKeyRequest>(request => new MemoryKeyRow { Code = request.Key, Title = request.Title, Version = 0 })
                .AllowWrite(row => row.Code).AllowWrite(row => row.Version);
            var patch = entity.HasCommand(save ? "Patch" : "StagePatch").Where<MemoryKeyRequest>((row, request) => row.Code == request.Key)
                .Patch(setters => setters.SetProperty<string, MemoryKeyRequest>(row => row.Title, (row, request) => request.Title));
            var delete = entity.HasCommand(save ? "Delete" : "StageDelete").Where<MemoryKeyRequest>((row, request) => row.Code == request.Key).TrackedDelete();
            if (keyed)
            {
                create.UseContextKey<MemoryKeyRequest, string>(request => request.Context);
                patch.UseContextKey<MemoryKeyRequest, string>(request => request.Context);
                delete.UseContextKey<MemoryKeyRequest, string>(request => request.Context);
            }
            if (save) { create.Save(); patch.Save(); delete.Save(); }
            else { create.Stage(); patch.Stage(); delete.Stage(); }
        }
        return builder;
    }

    internal static Assembly Compile(RepositoryModelBuilder builder)
    {
        using var context = Context(); return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }
    internal static (object Fixture, object Repository) Fixture(Assembly assembly)
    {
        const string ns = "Generated.MemoryKeys.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "MemoryKeyContextRepositoryFixture")!)!;
        return (fixture, Activator.CreateInstance(assembly.GetType(ns + "MemoryKeyRowRepositoryTestDouble")!, fixture)!);
    }
    internal static InMemoryWriteDataContext Data(object fixture, string? key = null) =>
        (InMemoryWriteDataContext)(key is null ? fixture.GetType().GetProperty("Data")!.GetValue(fixture)! : fixture.GetType().GetMethod("For")!.Invoke(fixture, [key])!);
    internal static InMemoryStore<MemoryKeyData, string> Store(InMemoryWriteDataContext data) =>
        (InMemoryStore<MemoryKeyData, string>)data.GetType().GetProperty("MemoryKeyRowStore")!.GetValue(data)!;
}
