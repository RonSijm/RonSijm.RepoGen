using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NavigationReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProxyNavigationContractTests
{
    [Fact]
    public async Task SharedNamedContractsCompareMappedGraphsWithActualGeneratedSqliteRepositories()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new NavigationReadContext(new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var libraries = Libraries(); foreach (var library in libraries) library.Books = [];
        context.AddRange(libraries); context.AddRange(Books());
        context.AddRange(new NavigationReadAuthor { Id = 1, Name = "Ada" }, new NavigationReadAuthor { Id = 2, Name = "Bob" });
        context.Add(new NavigationReadProfile { Id = 1, LibraryId = 1, Caption = "Main" });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var model = RepositoryModelFactory.Create(context, typeof(NavigationReadContext).Assembly, design: new ProxyNavigationRepositoryDesigner().CreateModel());
        var assembly = ProxyNavigationTestHarness.CompileProduction(model);
        var realLibrary = Activator.CreateInstance(assembly.GetType("Generated.ProxyNavigation.Persistence.NavigationReadLibraryRepositoryOperations")!, context)!;
        var realBook = Activator.CreateInstance(assembly.GetType("Generated.ProxyNavigation.Persistence.NavigationReadBookRepositoryOperations")!, context)!;
        var (memoryLibrary, memoryBook, _) = ProxyNavigationTestHarness.Repositories();
        async Task Equal<T>(object real, object memory, string name, params object?[] inputs)
            => JsonSerializer.Serialize(await Invoke<Task<T>>(memory, name, inputs)).Should().Be(JsonSerializer.Serialize(await Invoke<Task<T>>(real, name, inputs)), name);
        await Equal<IReadOnlyList<NavigationReadCard>>(realLibrary, memoryLibrary, "CardsAsync");
        await Equal<NavigationReadDetail?>(realLibrary, memoryLibrary, "DetailAsync", 1);
        await Equal<NavigationReadDetail?>(realLibrary, memoryLibrary, "DetailAsync", 3);
        await Equal<int?>(realLibrary, memoryLibrary, "FindAsync", 1);
        await Equal<int?>(realLibrary, memoryLibrary, "FindAsync", 99);
        await Equal<IReadOnlyList<int>>(realLibrary, memoryLibrary, "SearchAsync", 20);
        await Equal<PagedResult<List<string>>>(realLibrary, memoryLibrary, "NestedAsync", 2, 1);
        await Equal<IReadOnlyList<string>>(realBook, memoryBook, "ReferencesAsync");
        await Equal<IReadOnlyList<string>>(realBook, memoryBook, "SourceAsync");
        await Equal<IReadOnlyList<int>>(realBook, memoryBook, "ParentsAsync");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
