using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class NavigationReadTestHarness
{
    private static readonly Lazy<Assembly> Generated = new(() =>
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new NavigationReadRepositoryDesigner()));
    });
    internal static NavigationReadContext Context() => new(new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder Builder(bool strict = false) => new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
        .UseClosedRepositories("Generated.Navigation.Contracts").UseGeneratedNamespace("Generated.Navigation.Persistence")
        .UseScriptedTestDoubles("Generated.Navigation.TestDoubles").UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries(strict).GenerateOnlyConfiguredEntities());
    internal static (object Library, object Book, InMemoryDataContext Data) Repositories()
    {
        var fixture = Activator.CreateInstance(Generated.Value.GetType("Generated.Navigation.TestDoubles.NavigationReadContextRepositoryFixture")!)!;
        var library = Activator.CreateInstance(Generated.Value.GetType("Generated.Navigation.TestDoubles.NavigationReadLibraryRepositoryTestDouble")!, fixture)!;
        var book = Activator.CreateInstance(Generated.Value.GetType("Generated.Navigation.TestDoubles.NavigationReadBookRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        Seed(data);
        return (library, book, data);
    }
    internal static void Seed(InMemoryDataContext data)
    {
        Store<NavigationReadLibrary>(data).SeedRange(Libraries());
        Store<NavigationReadBook>(data).SeedRange(Books());
        Store<NavigationReadAuthor>(data).SeedRange([new() { Id = 1, Name = "Ada" }, new() { Id = 2, Name = "Bob" }]);
        Store<NavigationReadProfile>(data).Seed(new() { Id = 1, LibraryId = 1, Caption = "Main" });
    }
    internal static NavigationReadLibrary[] Libraries() =>
    [
        new() { Id = 1, Code = "same", Region = "north", Name = "North", Books = [new() { Id = 99, Price = 9999 }] },
        new() { Id = 2, Code = "same", Region = "south", Name = "South" },
        new() { Id = 3, Code = "hidden", Region = "north", Name = "Hidden", Hidden = true },
        new() { Id = 4, Code = "empty", Region = "north", Name = "Empty" }
    ];
    internal static NavigationReadBook[] Books() =>
    [
        new() { Id = 1, LibraryCode = "same", LibraryRegion = "north", AuthorId = 1, Price = 10 },
        new() { Id = 2, LibraryCode = "same", LibraryRegion = "north", AuthorId = 2, Price = 20, Archived = true, ParentId = 1 },
        new() { Id = 3, LibraryCode = "same", LibraryRegion = "south", AuthorId = 2, Price = 30 },
        new() { Id = 4, LibraryCode = "hidden", LibraryRegion = "north", AuthorId = 1, Price = 40 },
        new() { Id = 5, LibraryCode = "same", LibraryRegion = null, AuthorId = 1, Price = 50 }
    ];
    internal static InMemoryStore<TRow, int> Store<TRow>(InMemoryDataContext data) where TRow : class =>
        (InMemoryStore<TRow, int>)data.GetType().GetProperty(typeof(TRow).Name + "Store")!.GetValue(data)!;
    internal static TResult Invoke<TResult>(object repository, string name, params object?[] inputs) =>
        (TResult)repository.GetType().GetMethod(name)!.Invoke(repository, inputs.Append((object)CancellationToken.None).ToArray())!;
}
