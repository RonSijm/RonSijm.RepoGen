using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class ScriptedTestDoubleTestHarness
{
    internal static ScriptedContext Context() => new(new DbContextOptionsBuilder<ScriptedContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryGenerationModel Model(ScriptedContext context, bool enabled = true, bool ports = true)
        => RepositoryModelFactory.Create(context, typeof(ScriptedContext).Assembly, design: new ScriptedRowsRepositoryDesigner(enabled, ports).CreateModel());
    internal static RepositoryModelBuilder Builder() => new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
        .UseClosedRepositories("Generated.Scripted.Contracts").UseGeneratedNamespace("Generated.Scripted.Persistence")
        .UseScriptedTestDoubles("Generated.Scripted.TestDoubles").GenerateOnlyConfiguredEntities());

    internal static Assembly Compile(RepositoryGenerationBundle bundle, string? consumer = null)
    {
        // Load only contracts + test output. No context, production repository or database is instantiated.
        var files = bundle.Files.Where(static file => file.Kind is RepositoryOutputKind.Contracts or RepositoryOutputKind.TestDoubles)
            .Select(static file => CSharpSyntaxTree.ParseText(file.Contents));
        if (consumer is not null) files = files.Append(CSharpSyntaxTree.ParseText(consumer));
        var references = TestingReferences().Append(typeof(ScriptedView).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("Scripted_" + Guid.NewGuid().ToString("N"), files, references, CompilerOptions());
        using var output = new MemoryStream();
        var emit = compilation.Emit(output);
        emit.Diagnostics.Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(static diagnostic => diagnostic.ToString()).Should().BeEmpty();
        output.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(output);
    }

    internal static string[] TestingReferences() => ClosedRepositoryTests.PlatformReferences().Select(static reference => reference.Display!)
        .Concat([typeof(PagedResult<>).Assembly.Location, typeof(ScriptedOperation<,>).Assembly.Location, typeof(IServiceCollection).Assembly.Location])
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static CSharpCompilationOptions CompilerOptions() => new(OutputKind.DynamicallyLinkedLibrary,
        nullableContextOptions: NullableContextOptions.Enable, specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["CS1701"] = ReportDiagnostic.Suppress });

    internal static object Fixture(Assembly assembly) => Activator.CreateInstance(assembly.GetType("Generated.Scripted.TestDoubles.ScriptedRowRepositoryTestDouble")!)!;
    internal static void Returns(object fixture, string operation, object? value)
    {
        var script = fixture.GetType().GetProperty(operation)!.GetValue(fixture)!;
        script.GetType().GetMethod("Returns")!.Invoke(script, [value]);
    }
}
