using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class ProxyNavigationTestHarness
{
    private static readonly Lazy<Assembly> Generated = new(() =>
    {
        using var context = NavigationReadTestHarness.Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new ProxyNavigationRepositoryDesigner()));
    });
    internal static RepositoryModelBuilder Builder(bool strict = true) => new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
        .UseClosedRepositories("Generated.ProxyNavigation.Contracts").UseGeneratedNamespace("Generated.ProxyNavigation.Persistence")
        .UseScriptedTestDoubles("Generated.ProxyNavigation.TestDoubles").UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries(strict).GenerateOnlyConfiguredEntities());
    internal static (object Library, object Book, InMemoryDataContext Data) Repositories()
        => Repositories(Generated.Value);
    internal static (object Library, object Book, InMemoryDataContext Data) Repositories(Assembly assembly)
    {
        const string ns = "Generated.ProxyNavigation.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "NavigationReadContextRepositoryFixture")!)!;
        var library = Activator.CreateInstance(assembly.GetType(ns + "NavigationReadLibraryRepositoryTestDouble")!, fixture)!;
        var book = Activator.CreateInstance(assembly.GetType(ns + "NavigationReadBookRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        Seed(data);
        return (library, book, data);
    }
    internal static void Seed(InMemoryDataContext data)
    {
        Store<ProxyNavigationLibraryData>(data, nameof(NavigationReadLibrary)).SeedRange(NavigationReadTestHarness.Libraries()
            .Select(row => new ProxyNavigationLibraryData(row.Id, row.Code, row.Region, row.Name, row.Hidden)));
        Store<ProxyNavigationBookData>(data, nameof(NavigationReadBook)).SeedRange(NavigationReadTestHarness.Books()
            .Select(row => new ProxyNavigationBookData(row.Id, row.LibraryCode, row.LibraryRegion, row.AuthorId, row.Price, row.Archived, row.ParentId)));
        Store<ProxyNavigationAuthorData>(data, nameof(NavigationReadAuthor)).SeedRange([new(1, "Ada"), new(2, "Bob")]);
        Store<ProxyNavigationProfileData>(data, nameof(NavigationReadProfile)).Seed(new(1, 1, "Main"));
    }
    internal static InMemoryStore<TData, int> Store<TData>(InMemoryDataContext data, string entity) where TData : class
        => (InMemoryStore<TData, int>)data.GetType().GetProperty(entity + "Store")!.GetValue(data)!;

    internal static Assembly CompileProduction(RepositoryGenerationModel model)
    {
        var sources = new[] { RepositorySourceRenderer.Render(model), ClosedRepositorySourceRenderer.RenderContracts(model) };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(NavigationReadContext).Assembly.Location).Append(typeof(DbContext).Assembly.Location)
            .Append(typeof(RelationalQueryableExtensions).Assembly.Location).Append(typeof(PagedResult<>).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ProxyNavigationProduction_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            references, ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream();
        var emit = compilation.Emit(output);
        emit.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
        output.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(output);
    }
}
