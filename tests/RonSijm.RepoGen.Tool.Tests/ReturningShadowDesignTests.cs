using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.SqlServer;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ReturningShadowTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningShadowDesignTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void EveryOutputLayoutCompilesAndKeepsTheScalarBufferOutOfApplicationContracts(RepositoryOutputLayout layout)
    {
        using var context = Empty(); var bundle = RepositoryGenerator.Generate(context, Builder().Build(), layout);
        var sources = bundle.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts).Select(file => file.Contents);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(ReturningShadowRow).Assembly.Location).Append(typeof(SqliteReturningWriter).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ShadowLayout", sources.Select(source => CSharpSyntaxTree.ParseText(source)), references, ScriptedTestDoubleTestHarness.CompilerOptions());
        using var stream = new MemoryStream(); var result = compilation.Emit(stream);
        result.Diagnostics.Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(item => item.ToString()).Should().BeEmpty();
        var contracts = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.Contracts).Select(file => file.Contents));
        contracts.Should().NotContain("EntityFrameworkCore").And.NotContain("ReturningWriteRow").And.NotContain("EF.Property");
        var persistence = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.Repositories).Select(file => file.Contents));
        persistence.Should().Contain("ExecuteWithStateAsync").And.Contain("__repoGenReturnedRow.GetValue<");
        using var manifest = JsonDocument.Parse(bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents);
        var state = manifest.RootElement.GetProperty("entities")[0].GetProperty("commands")[0].GetProperty("returningPropertyValues");
        state.GetProperty("fixtureSimulation").GetBoolean().Should().BeFalse();
        state.GetProperty("properties").EnumerateArray().Should().Contain(property => property.GetProperty("name").GetString() == "Normalized" && property.GetProperty("isShadow").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShadowTablesRemainUnsupportedWithoutThePerCommandPermissionOrAfterItIsDisabled(bool reset)
    {
        using var context = Empty(); var builder = Minimal();
        var command = builder.Entity<ReturningShadowRow>().HasCommand("Remove").Delete().ReturnsMany(row => row.Id);
        if (reset) command.IncludeShadowProperties().IncludeShadowProperties(false);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1306:*");
        context.Database.GetDbConnection().State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("wrongType")]
    [InlineData("requestName")]
    [InlineData("wrongReceiver")]
    [InlineData("opaqueEntity")]
    public void NamedReturnedPropertiesRequireLiteralExactMappedScalarReadsOfTheCurrentRow(string shape)
    {
        using var context = Empty(); var builder = Minimal();
        var command = builder.Entity<ReturningShadowRow>().HasCommand("Remove").Delete().IncludeShadowProperties();
        switch (shape)
        {
            case "unknown": command.ReturnsMany(row => EF.Property<int>(row, "Unknown")); break;
            case "wrongType": command.ReturnsMany(row => EF.Property<long>(row, "ComputedVersion")); break;
            case "requestName": command.ReturnsMany<string, string>((row, name) => EF.Property<string>(row, name)); break;
            case "wrongReceiver": command.ReturnsMany(row => EF.Property<string>(new ReturningShadowRow(), "Normalized")); break;
            case "opaqueEntity": command.ReturnsMany(row => ReturningShadowCalls.WholeEntity(row)); break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1306:*");
    }

    [Theory]
    [InlineData("field")]
    [InlineData("indexer")]
    [InlineData("trigger")]
    public void ShadowPermissionDoesNotEnableUnsupportedMappings(string shape)
    {
        using var context = Empty(shape); var builder = Minimal();
        builder.Entity<ReturningShadowRow>().HasCommand("Remove").Delete().ReturnsMany(row => row.Id).IncludeShadowProperties();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1306:*");
        Action validate = () => RelationalReturningWriter.ValidateModel(context.Model.FindEntityType(typeof(ReturningShadowRow))!, includeShadowProperties: true);
        validate.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void NativeStateMaterializationStillRequiresClrKeys()
    {
        using var context = Empty("shadowKey");
        Action validate = () => RelationalReturningWriter.ValidateModel(context.Model.FindEntityType(typeof(ReturningShadowRow))!, includeShadowProperties: true);
        validate.Should().Throw<NotSupportedException>().WithMessage("*CLR keys*");
    }

    [Theory]
    [InlineData("count")]
    [InlineData("tracked")]
    public void ThePermissionCannotBeAppliedToOtherCommandPaths(string shape)
    {
        using var context = Empty(); var builder = Minimal(); var command = builder.Entity<ReturningShadowRow>().HasCommand("Remove");
        if (shape == "tracked") command.TrackedDelete().Stage(); else command.Delete();
        command.IncludeShadowProperties();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1306:*only*native ReturnsMany*");
    }

    [Fact]
    public void ExistingThirdPartyWritersMustExplicitlyImplementDetachedStateSupport()
    {
        using var context = Empty(); var builder = Minimal().Configure(options => options.UseReturningWrites<ReturningLegacyWriter>());
        builder.Entity<ReturningShadowRow>().HasCommand("Remove").Delete().ReturnsMany(row => row.Id).IncludeShadowProperties();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1307:*IRepositoryReturningStateWriter*");
    }

    [Fact]
    public void SqlServerGenerationUsesTheSameDetachedStateContract()
    {
        using var context = new ReturningShadowContext(new DbContextOptionsBuilder<ReturningShadowContext>().UseSqlServer("Server=unused;Database=unused")
            .ReplaceService<IModelCacheKeyFactory, ReturningShadowModelCacheKeyFactory>().Options);
        var builder = Minimal().Configure(options => options.UseReturningWrites<SqlServerReturningWriter>());
        builder.Entity<ReturningShadowRow>().HasCommand("Remove").Delete().IncludeShadowProperties().ReturnsMany(row => EF.Property<string>(row, "Normalized"));
        var model = RepositoryModelFactory.Create(context, typeof(ReturningShadowRow).Assembly, design: builder.Build());
        ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        context.Database.GetDbConnection().State.Should().Be(System.Data.ConnectionState.Closed);
    }

    internal static RepositoryModelBuilder Minimal() => new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
        .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence").UseReturningWrites<SqliteReturningWriter>());
}
