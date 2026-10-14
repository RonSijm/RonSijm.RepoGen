using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class InMemorySourceQueryTestHarness
{
    private static readonly Lazy<Assembly> Generated = new(() =>
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new PortableSourceRepositoryDesigner()));
    });

    internal static PortableSourceContext Context() => new(new DbContextOptionsBuilder<PortableSourceContext>().UseSqlite("Data Source=:memory:").Options);

    internal static object Repository()
    {
        var assembly = Generated.Value;
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.Sources.TestDoubles.PortableSourceContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.Sources.TestDoubles.PortableSourceOrderRepositoryTestDouble")!, fixture)!;
        var data = Data(repository);
        data.GetType().GetProperty("PortableSourceOrderStore")!.GetValue(data).AsStore<PortableSourceOrder>().SeedRange(Orders());
        data.GetType().GetProperty("PortableSourceCustomerStore")!.GetValue(data).AsStore<PortableSourceCustomer>().SeedRange(Customers());
        return repository;
    }

    internal static InMemoryDataContext Data(object repository)
    {
        var fixture = repository.GetType().GetProperty("Fixture")!.GetValue(repository)!;
        return (InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
    }

    internal static InMemoryStore<TEntity, int> AsStore<TEntity>(this object? store) where TEntity : class => (InMemoryStore<TEntity, int>)store!;

    internal static PortableSourceOrder[] Orders() =>
    [
        new() { Id = 1, CustomerId = 1, Zone = "N", Amount = 10 },
        new() { Id = 2, CustomerId = 1, Zone = "S", Amount = 20 },
        new() { Id = 3, CustomerId = 3, Zone = "N", Amount = 30 },
        new() { Id = 4, CustomerId = 99, Zone = "N", Amount = 40 },
        new() { Id = 5, CustomerId = 2, Zone = "S", Amount = 50 },
        new() { Id = 6, CustomerId = 1, Zone = "N", Amount = 60 },
        new() { Id = 7, CustomerId = 1, Zone = "N", Amount = 70, Hidden = true }
    ];

    internal static PortableSourceCustomer[] Customers() =>
    [
        new() { Id = 1, Name = "North", Zone = "N" },
        new() { Id = 2, Name = "South", Zone = "S" },
        new() { Id = 3, Name = "Hidden", Zone = "N", Hidden = true }
    ];

    internal static TResult Invoke<TResult>(object repository, string name, params object?[] inputs)
        => (TResult)repository.GetType().GetMethod(name)!.Invoke(repository, inputs.Append((object)CancellationToken.None).ToArray())!;
}
