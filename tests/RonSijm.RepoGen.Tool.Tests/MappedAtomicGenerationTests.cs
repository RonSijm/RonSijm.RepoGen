using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.SqlServer;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedAtomicTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedAtomicGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    public void BothProvidersCompileAllLayoutsWithScalarOnlyClosedPorts(RepositoryOutputLayout layout, bool sqlServer)
    {
        using var context = Context(sqlServer);
        var bundle = RepositoryGenerator.Generate(context, Builder(sqlServer: sqlServer).Build(), layout);
        var sources = bundle.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts).ToArray();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(MappedAtomicContext).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("MappedAtomicLayout", sources.Select(file => CSharpSyntaxTree.ParseText(file.Contents)), references,
            ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream(); var result = compilation.Emit(output);
        result.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
        ScriptedTestDoubleTestHarness.Compile(bundle);
        var contracts = string.Join("\n", sources.Where(file => file.Kind == RepositoryOutputKind.Contracts).Select(file => file.Contents));
        contracts.Should().NotContain("EntityFrameworkCore").And.NotContain("ReturningWriteRow").And.NotContain("RonSijm.RepoGen.Tool.Tests.MappedStateNote");
        var persistence = string.Join("\n", sources.Where(file => file.Kind == RepositoryOutputKind.Repositories).Select(file => file.Contents));
        persistence.Should().Contain("ExecuteWithMappedStateAsync").And.Contain("__repoGenUpdated.Value").And.Contain("__repoGenReturnedRow.GetValue");
        var testing = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        CSharpSyntaxTree.ParseText(testing).GetRoot().DescendantTokens().Where(token => token.RawKind == (int)SyntaxKind.IdentifierToken)
            .Select(token => token.ValueText).Should().NotContain("EntityFrameworkCore").And.NotContain("ReturningWriteRow").And.NotContain("__repoGenReturnedRow");
        context.Database.GetDbConnection().State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Fact]
    public void ReceiptsDistinguishAtomicBuffersFreshReadsAndIndependentFixtureConsent()
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(commands: false).Build());
        enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents)
            .Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        var manifest = enabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("NativeInsertBufferOrUpdatedQueryScalarBuffer").And.Contain("SuccessfulInsertedOrUpdatedScalarsOnly")
            .And.Contain("MapBeforeOwnedCommitCallerRollbackForBorrowedTransaction").And.Contain("ExplicitInsertOrUpdateFactoriesSavedRowsOnly");
        using var parsed = JsonDocument.Parse(manifest);
        var generated = parsed.RootElement.GetProperty("testDoubles").GetProperty("repositories")[0].GetProperty("operations");
        generated.EnumerateArray().Should().OnlyContain(operation => operation.GetProperty("status").GetString() == "Generated");
        disabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("returningPropertyState");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThirdPartyAtomicWritersMustAdvertiseMappedCapture(bool shadowOnly)
    {
        using var context = Context(); var builder = Builder(commands: false);
        if (shadowOnly) builder.Configure(options => options.UseAtomicWrites<AtomicShadowOnlyWriter>());
        else builder.Configure(options => options.UseAtomicWrites<TimeoutAtomicWriter>());
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1308:*IRepositoryAtomicMappedStateWriter*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NarrowOrRemovedPermissionCannotMaterializeFieldsAndIndexers(bool shadow)
    {
        using var context = Context(); var builder = Builder(commands: false);
        var entity = builder.Entity<MappedStateNote>();
        entity.HasCommand("Narrow").Create<int>(id => new MappedStateNote { Id = id, Title = "narrow", Counter = 0 })
            .AllowWrite(row => row.Id).InsertIfAbsent(row => row.Id).Returns(row => row.Id)
            .IncludeMappedProperties().IncludeMappedProperties(false).IncludeShadowProperties(shadow);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task ImmediateAndStateConsentRemainIndependentEvenOnEmptyStores()
    {
        using var context = Context(); var builder = Builder(simulate: false, strict: false);
        var (repository, _, store) = Fixture(Compile(builder)); store.Clear();
        Func<Task> call = () => Call<CommandResult<MappedStateCard>>(repository, "Ensure", new(20, "new"));
        await call.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        builder.Configure(options => options.RequirePortableTestCommands());
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        ScriptedTestDoubleTestHarness.Returns(repository, "OnEnsureAsync", new CommandResult<MappedStateCard>(CommandStatus.AlreadyExists));
        (await Call<CommandResult<MappedStateCard>>(repository, "Ensure", new(20, "new"))).Status.Should().Be(CommandStatus.AlreadyExists);
        generate = () => RepositoryGenerator.Generate(context, Builder(state: false).Build());
        generate.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task CapturedWritesDoNotDependOnAutomaticReadGeneration()
    {
        var (repository, _, store) = Fixture(Compile(Builder(queries: false)));
        var result = await Call<CommandResult<MappedStateCard>>(repository, "Ensure", new(20, "new"));
        result.Value!.Revision.Should().Be(2);
        store.Find(20)!.Caption.Should().Be("NEW");
    }

    [Fact]
    public void ReadFilterConsentDoesNotGrantFilteredAtomicWriteExecution()
    {
        using var context = Context();
        Action generate = () => RepositoryGenerator.Generate(context, Builder(filter: false).Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*filters*");
    }

    [Theory]
    [InlineData("readonly")]
    [InlineData("fieldKey")]
    [InlineData("indexerKey")]
    [InlineData("trigger")]
    public void ProductionCaptureKeepsTheExistingKeyMaterializationAndTableGuards(string shape)
    {
        using var context = ReturningMappedTestHarness.Empty(shape);
        var builder = ReturningShadowDesignTests.Minimal().Configure(options => options.UseAtomicWrites<RonSijm.RepoGen.Sqlite.SqliteAtomicWriter>());
        builder.Entity<ReturningMappedRow>().HasCommand("Ensure").Create<int>(id => new ReturningMappedRow { Id = id, Title = "new" })
            .AllowWrite(row => row.Id).InsertIfAbsent(row => row.Id).Returns(row => row.Id).IncludeMappedProperties();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1306:*");
    }

    [Fact]
    public void SqlServerCaptureValidationRemainsOfflineAndExplicit()
    {
        using var context = Context(sqlServer: true);
        var entity = context.Model.FindEntityType(typeof(MappedStateNote))!;
        Action ordinary = () => SqlServerAtomicWriter.ValidateModel(entity);
        ordinary.Should().Throw<NotSupportedException>();
        Action shadow = () => SqlServerAtomicWriter.ValidateModel(entity, includeShadowProperties: true);
        shadow.Should().Throw<NotSupportedException>();
        SqlServerAtomicWriter.ValidateModel(entity, includeMappedProperties: true);
        context.Database.GetDbConnection().State.Should().Be(System.Data.ConnectionState.Closed);
    }
}
