using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class MemoryValueTestHarness
{
    internal static MemoryValueContext Context() => new(new DbContextOptionsBuilder<MemoryValueContext>().UseSqlite("Data Source=:memory:").Options);
    internal static (object Repository, InMemoryWriteDataContext Data, InMemoryStore<MemoryValueRow, int> Store) Fixture(Assembly assembly)
    {
        const string ns = "Generated.MemoryValues.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "MemoryValueContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType(ns + "MemoryValueRowRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = (InMemoryStore<MemoryValueRow, int>)data.GetType().GetProperty("MemoryValueRowStore")!.GetValue(data)!;
        store.Seed(new() { Id = 7, Title = "old", TitleLength = 3, Label = "fixture" });
        return (repository, data, store);
    }

    internal static Assembly CompileProduction(MemoryValueContext context)
    {
        var model = RepositoryModelFactory.Create(context, typeof(MemoryValueContext).Assembly, design: new MemoryValueRepositoryDesigner().CreateModel());
        var sources = new[] { RepositorySourceRenderer.Render(model), ClosedRepositorySourceRenderer.RenderContracts(model) };
        var references = ScriptedTestDoubleTestHarness.TestingReferences().Append(typeof(MemoryValueContext).Assembly.Location)
            .Append(typeof(DbContext).Assembly.Location).Append(typeof(RelationalQueryableExtensions).Assembly.Location)
            .Append(typeof(RonSijm.RepoGen.EntityFrameworkCore.TrackedBatchCommandExecutor).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("MemoryValueProduction_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)), references,
            ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream(); var emitted = compilation.Emit(output);
        emitted.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).Should().BeEmpty();
        output.Position = 0; return AssemblyLoadContext.Default.LoadFromStream(output);
    }
}
