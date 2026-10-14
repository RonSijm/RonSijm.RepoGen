using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class InMemoryQueryTestHarness
{
    private static readonly Lazy<Assembly> Generated = new(() =>
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new PortableReadRepositoryDesigner()));
    });

    internal static PortableReadContext Context() => new(new DbContextOptionsBuilder<PortableReadContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder Builder(bool strict = false) => new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
        .UseClosedRepositories("Generated.Portable.Contracts").UseGeneratedNamespace("Generated.Portable.Persistence")
        .UseScriptedTestDoubles("Generated.Portable.TestDoubles").UseInMemoryTestStores().UseInMemoryTestQueries()
        .RequirePortableTestQueries(strict).GenerateOnlyConfiguredEntities());
    internal static RepositoryEntityBuilder<PortableRow> Entity(RepositoryModelBuilder builder)
    {
        var entity = builder.Entity<PortableRow>();
        entity.WriteEnabled = false;
        return entity.HasInMemoryStore(row => row.Id, row => new PortableRow { Id = row.Id, Name = row.Name, Category = row.Category, Price = row.Price, Hidden = row.Hidden });
    }
    internal static object Repository()
    {
        var assembly = Generated.Value;
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.Portable.TestDoubles.PortableReadContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.Portable.TestDoubles.PortableRowRepositoryTestDouble")!, fixture)!;
        Store(repository).SeedRange(Seeds());
        return repository;
    }
    internal static InMemoryStore<PortableRow, int> Store(object repository)
    {
        var fixture = repository.GetType().GetProperty("Fixture")!.GetValue(repository)!;
        var data = fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        return (InMemoryStore<PortableRow, int>)data.GetType().GetProperty("PortableRowStore")!.GetValue(data)!;
    }
    internal static PortableRow[] Seeds() =>
    [
        new() { Id = 3, Name = "pear", Category = "fruit", Price = 20 },
        new() { Id = 1, Name = "apple", Category = "fruit", Price = 10 },
        new() { Id = 2, Name = "paper", Category = "supplies", Price = 30 },
        new() { Id = 4, Name = "hidden", Category = "fruit", Price = 100, Hidden = true }
    ];
    internal static TResult Invoke<TResult>(object repository, string name, params object?[] inputs)
        => (TResult)repository.GetType().GetMethod(name)!.Invoke(repository, inputs.Append((object)CancellationToken.None).ToArray())!;
}
