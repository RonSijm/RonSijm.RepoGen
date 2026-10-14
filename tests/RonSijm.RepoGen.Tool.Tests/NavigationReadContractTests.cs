using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NavigationReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NavigationReadContractTests
{
    [Fact]
    public async Task SameNestedNavigationOperationsRunAgainstFixturesAndActualGeneratedSqliteRepositories()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new NavigationReadContext(new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var libraries = Libraries();
        foreach (var library in libraries) library.Books = []; // Invalid pre-seeded graph is not a relational row.
        context.AddRange(libraries);
        context.AddRange(new NavigationReadAuthor { Id = 1, Name = "Ada" }, new NavigationReadAuthor { Id = 2, Name = "Bob" });
        context.AddRange(Books());
        context.Add(new NavigationReadProfile { Id = 1, LibraryId = 1, Caption = "Main" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var model = RepositoryModelFactory.Create(context, typeof(NavigationReadContext).Assembly, design: new NavigationReadRepositoryDesigner().CreateModel());
        var assembly = Compile(model);
        var productionLibrary = Activator.CreateInstance(assembly.GetType("Generated.Navigation.Persistence.NavigationReadLibraryRepositoryOperations")!, context)!;
        var productionBook = Activator.CreateInstance(assembly.GetType("Generated.Navigation.Persistence.NavigationReadBookRepositoryOperations")!, context)!;
        var (memoryLibrary, memoryBook, _) = Repositories();
        async Task Equal<TResult>(object real, object memory, string name, params object?[] inputs)
        {
            var expected = await Invoke<Task<TResult>>(real, name, inputs);
            var actual = await Invoke<Task<TResult>>(memory, name, inputs);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected), name);
        }
        await Equal<IReadOnlyList<NavigationReadCard>>(productionLibrary, memoryLibrary, "CardsAsync");
        await Equal<NavigationReadDetail?>(productionLibrary, memoryLibrary, "DetailAsync", 1);
        await Equal<NavigationReadDetail?>(productionLibrary, memoryLibrary, "DetailAsync", 3);
        await Equal<int?>(productionLibrary, memoryLibrary, "FindAsync", 1);
        await Equal<int?>(productionLibrary, memoryLibrary, "FindAsync", 99);
        await Equal<IReadOnlyList<int>>(productionLibrary, memoryLibrary, "SearchAsync", 20);
        await Equal<PagedResult<List<string>>>(productionLibrary, memoryLibrary, "NestedAsync", 2, 1);
        await Equal<IReadOnlyList<string>>(productionBook, memoryBook, "ReferencesAsync");
        await Equal<IReadOnlyList<string>>(productionBook, memoryBook, "SourceAsync");
        await Equal<IReadOnlyList<int>>(productionBook, memoryBook, "ParentsAsync");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    private static Assembly Compile(RepositoryGenerationModel model)
    {
        var sources = new[] { RepositorySourceRenderer.Render(model), ClosedRepositorySourceRenderer.RenderContracts(model) };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(NavigationReadContext).Assembly.Location).Append(typeof(DbContext).Assembly.Location)
            .Append(typeof(RelationalQueryableExtensions).Assembly.Location).Append(typeof(PagedResult<>).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("NavigationProduction_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            references, ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream();
        var emit = compilation.Emit(output);
        emit.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
        output.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(output);
    }
}
