using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class ProxyWriteTestHarness
{
    private static readonly Lazy<Assembly> Generated = new(() =>
    {
        using var context = Context(); return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new ProxyWriteRepositoryDesigner()));
    });
    internal static ProxyWriteContext Context() => new(new DbContextOptionsBuilder<ProxyWriteContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder Builder(bool strict = true) => new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
        .UseClosedRepositories("Generated.ProxyWrites.Contracts").UseGeneratedNamespace("Generated.ProxyWrites.Persistence")
        .UseScriptedTestDoubles("Generated.ProxyWrites.TestDoubles").UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
        .RequirePortableTestCommands(strict).GenerateOnlyConfiguredEntities());
    internal static (object Repository, object Audit, InMemoryWriteDataContext Data, InMemoryStore<ProxyWriteData, int> Store) Fixture(Assembly? assembly = null)
    {
        assembly ??= Generated.Value;
        const string ns = "Generated.ProxyWrites.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "ProxyWriteContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType(ns + "ProxyWriteRecordRepositoryTestDouble")!, fixture)!;
        var audit = Activator.CreateInstance(assembly.GetType(ns + "ProxyWriteAuditRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = (InMemoryStore<ProxyWriteData, int>)data.GetType().GetProperty("ProxyWriteRecordStore")!.GetValue(data)!;
        store.SeedRange([new(7, "old", 3, "fixture", false, [1, 2]), new(8, "hidden", 6, "fixture", true, [3])]);
        return (repository, audit, data, store);
    }
    internal static Task<TResult> Invoke<TResult>(object repository, string name, params object?[] inputs)
        => (Task<TResult>)repository.GetType().GetMethod(name)!.Invoke(repository, inputs.Append((object)CancellationToken.None).ToArray())!;
    internal static Assembly CompileProduction(ProxyWriteContext context)
    {
        var model = RepositoryModelFactory.Create(context, typeof(ProxyWriteContext).Assembly, design: new ProxyWriteRepositoryDesigner().CreateModel());
        var sources = new[] { RepositorySourceRenderer.Render(model), ClosedRepositorySourceRenderer.RenderContracts(model) };
        var references = ScriptedTestDoubleTestHarness.TestingReferences().Append(typeof(ProxyWriteContext).Assembly.Location)
            .Append(typeof(DbContext).Assembly.Location).Append(typeof(RelationalQueryableExtensions).Assembly.Location)
            .Append(typeof(RonSijm.RepoGen.EntityFrameworkCore.TrackedBatchCommandExecutor).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("RepoGenProxyWriteProduction", sources.Select(source => CSharpSyntaxTree.ParseText(source)), references,
            ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream(); var emitted = compilation.Emit(output);
        emitted.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
        output.Position = 0; return AssemblyLoadContext.Default.LoadFromStream(output);
    }
}
