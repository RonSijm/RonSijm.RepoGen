using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedPropertyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedPropertyGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    public void NamedBindingsCompileEveryLayoutWithInspectableOptInReceipts(RepositoryOutputLayout layout, bool closed)
    {
        using var context = Empty(); var builder = Builder(closed);
        if (closed) Versioned(builder, "Rename").ReturnsMany(row => EF.Property<int>(row, MappedPropertyContext.Token)).IncludeShadowProperties();
        else builder.Entity<MappedPropertyNote>().HasAccessor<string>("Code");
        if (!closed && layout == RepositoryOutputLayout.PerContract)
        {
            Action unsupported = () => RepositoryGenerator.Generate(context, builder.Build(), layout, generateSelectors: true);
            unsupported.Should().Throw<InvalidOperationException>().WithMessage("RGG0006:*");
            return;
        }
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout, generateSelectors: true);
        var files = bundle.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts or RepositoryOutputKind.Projections);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Append(typeof(MappedPropertyNote).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("MappedLayout", files.Select(file => CSharpSyntaxTree.ParseText(file.Contents)), references, ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream(); var result = compilation.Emit(output);
        result.Diagnostics.Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(item => item.ToString()).Should().BeEmpty();
        using var manifest = JsonDocument.Parse(bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents);
        var bindings = manifest.RootElement.GetProperty("entities")[0].GetProperty("mappedPropertyBindings");
        bindings.GetProperty(closed ? "expectedVersions" : "accessors").EnumerateArray().Should().Contain(item => item.GetProperty("shadow").GetBoolean());
        if (closed) string.Join("\n", files.Where(file => file.Kind == RepositoryOutputKind.Contracts).Select(file => file.Contents)).Should().NotContain("EF.Property").And.NotContain(MappedPropertyContext.Token);
    }

    [Fact]
    public void DefaultGenerationDoesNotInventMappedBindingsOrShadowIndexLookups()
    {
        using var context = Empty(); var builder = Builder(closed: false); builder.Entity<MappedPropertyNote>();
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("mappedPropertyBindings");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Repositories).Contents.Should().NotContain("ByCode");
    }
}
