using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class ProxyReadTestHarness
{
    private static readonly Lazy<Assembly> Generated = new(() =>
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new ProxyReadRepositoryDesigner()));
    });
    internal static ProxyReadContext Context() => new(new DbContextOptionsBuilder<ProxyReadContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder Builder(bool strict = false) => new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
        .UseClosedRepositories("Generated.Proxies.Contracts").UseGeneratedNamespace("Generated.Proxies.Persistence")
        .UseScriptedTestDoubles("Generated.Proxies.TestDoubles").UseInMemoryTestStores().UseInMemoryTestQueries()
        .RequirePortableTestQueries(strict).GenerateOnlyConfiguredEntities());
    internal static RepositoryEntityBuilder<ProxyReadEntity> Entity(RepositoryModelBuilder builder)
    {
        var entity = builder.Entity<ProxyReadEntity>();
        entity.WriteEnabled = false;
        return ProxyReadRepositoryDesigner.Store(entity);
    }
    internal static object Repository()
    {
        var assembly = Generated.Value;
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.Proxies.TestDoubles.ProxyReadContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.Proxies.TestDoubles.ProxyReadEntityRepositoryTestDouble")!, fixture)!;
        Store(repository).SeedRange(Rows());
        ((InMemoryStore<ProxyReadDepartmentData, int>)Data(repository).GetType().GetProperty("ProxyReadDepartmentStore")!.GetValue(Data(repository))!)
            .SeedRange([new(1, "Office"), new(2, "Hidden", true)]);
        return repository;
    }
    internal static InMemoryDataContext Data(object repository)
    {
        var fixture = repository.GetType().GetProperty("Fixture")!.GetValue(repository)!;
        return (InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
    }
    internal static InMemoryStore<ProxyReadData, int> Store(object repository) =>
        (InMemoryStore<ProxyReadData, int>)Data(repository).GetType().GetProperty("ProxyReadEntityStore")!.GetValue(Data(repository))!;
    internal static ProxyReadData[] Rows() =>
    [
        new() { Key = 3, Caption = "pear", Group = "fruit", RawScore = 20, DepartmentKey = 2, Stamp = [3] },
        new() { Key = 1, Caption = "apple", Group = "fruit", RawScore = 10, DepartmentKey = 1, Stamp = [1] },
        new() { Key = 2, Caption = "paper", Group = "supplies", RawScore = 30, DepartmentKey = 99, Stamp = [2] },
        new() { Key = 4, Caption = "hidden", Group = "fruit", RawScore = 100, DepartmentKey = 1, Archived = true, Stamp = [4] }
    ];
    internal static TResult Invoke<TResult>(object repository, string name, params object?[] inputs) =>
        (TResult)repository.GetType().GetMethod(name)!.Invoke(repository, inputs.Append((object)CancellationToken.None).ToArray())!;
}
