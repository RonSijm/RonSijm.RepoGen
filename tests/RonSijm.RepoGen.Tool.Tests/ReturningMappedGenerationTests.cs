using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ReturningMappedTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningMappedGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    public void BothProvidersCompileEveryLayoutWithoutExposingModelAccessToApplicationPorts(RepositoryOutputLayout layout, bool sqlServer)
    {
        using var context = Empty(sqlServer: sqlServer);
        var bundle = RepositoryGenerator.Generate(context, Builder(sqlServer: sqlServer).Build(), layout);
        var files = bundle.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts).ToArray();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Append(typeof(ReturningMappedRow).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("MappedReturningLayout", files.Select(file => CSharpSyntaxTree.ParseText(file.Contents)), references, ScriptedTestDoubleTestHarness.CompilerOptions());
        using var stream = new MemoryStream(); var result = compilation.Emit(stream);
        result.Diagnostics.Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(item => item.ToString()).Should().BeEmpty();
        string.Join("\n", files.Where(file => file.Kind == RepositoryOutputKind.Contracts).Select(file => file.Contents))
            .Should().NotContain("EntityFrameworkCore").And.NotContain("ReturningWriteRow").And.NotContain("RonSijm.RepoGen.Tool.Tests.ReturningMappedRow");
        string.Join("\n", files.Where(file => file.Kind == RepositoryOutputKind.Repositories).Select(file => file.Contents)).Should().Contain("ExecuteWithMappedStateAsync");
        context.Database.GetDbConnection().State.Should().Be(System.Data.ConnectionState.Closed);
    }
}
