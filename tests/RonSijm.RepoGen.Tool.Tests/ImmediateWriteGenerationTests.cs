using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ImmediateTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ImmediateWriteGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void ImmediateScalarCommandsCompileInEveryLayoutWithExplicitCoverageReceipts(RepositoryOutputLayout layout)
    {
        using var context = Context(); var bundle = RepositoryGenerator.Generate(context, Builder(mapped: true).Build(), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("ExecuteUpdateReturningAsync<").And.Contain("ExecuteDeleteReturningAsync<").And.Contain("ExecuteUpdateAsync<").And.Contain("ExecuteDeleteAsync<")
            .And.NotContain("Microsoft.EntityFrameworkCore").And.NotContain("new global::RonSijm.RepoGen.Tool.Tests.ImmediateRow");
        using var manifest = JsonDocument.Parse(bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents);
        var operations = manifest.RootElement.GetProperty("testDoubles").GetProperty("repositories")[0].GetProperty("operations").EnumerateArray().ToArray();
        var receipts = operations.Where(operation => operation.TryGetProperty("immediateSimulation", out _)).Select(operation => operation.GetProperty("immediateSimulation")).ToArray();
        receipts.Should().HaveCount(6);
        foreach (var receipt in receipts)
        {
            receipt.GetProperty("pendingWrites").GetString().Should().Be("NeitherFlushedNorRefreshed");
            receipt.GetProperty("relationalEquivalence").GetBoolean().Should().BeFalse();
        }
    }

    [Fact]
    public void OptInNeverChangesProductionOutputContractsOrApiAndCanBeTurnedOff()
    {
        using var context = Context(); var baseline = RepositoryGenerator.Generate(context, Builder(simulate: false, strict: false).Build());
        var enabled = RepositoryGenerator.Generate(context, Builder(strict: false).Build());
        enabled.Files.Where(file => file.Kind is not (RepositoryOutputKind.Manifest or RepositoryOutputKind.TestDoubles))
            .Should().BeEquivalentTo(baseline.Files.Where(file => file.Kind is not (RepositoryOutputKind.Manifest or RepositoryOutputKind.TestDoubles)));
        enabled.CreateApiSnapshot().ToJson().Should().Be(baseline.CreateApiSnapshot().ToJson());
        baseline.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("ExecuteUpdateReturningAsync<");
        baseline.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("immediateSimulation");
        var builder = Builder(strict: false, disableBump: true);
        var model = RepositoryModelFactory.Create(context, typeof(ImmediateContext).Assembly, design: builder.Build());
        model.Entities.Single().Commands.Single(command => command.MethodName == "BumpAsync").InMemoryCommand!.Supported.Should().BeFalse();
    }

    [Fact]
    public void StrictCommandsRequireExplicitImmediateSimulationOrAnAcknowledgedOverride()
    {
        using var context = Context(); var builder = Builder(simulate: false);
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*SimulateImmediateWrites*");
        builder = Builder(simulate: false, overrideImmediate: true);
        var bundle = RepositoryGenerator.Generate(context, builder.Build()); ScriptedTestDoubleTestHarness.Compile(bundle);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("RequiresOverride").And.NotContain("immediateSimulation");
    }

    [Theory]
    [InlineData("Commands")]
    [InlineData("Double")]
    [InlineData("ReadOnly")]
    [InlineData("Scripts")]
    public void DisabledAndReadOnlyFeaturesDoNotEmitImmediateBackends(string disabled)
    {
        using var context = Context(); var builder = Builder(mapped: true);
        switch (disabled)
        {
            case "Commands": builder.Configure(options => options.RequirePortableTestCommands(false).UseInMemoryTestCommands(false)); break;
            case "Double":
                builder.Entity<ImmediateRow>().GenerateTestDouble(false);
                builder.Configure(options => options.RequirePortableTestCommands(false).UseInMemoryTestCommands(false).UseInMemoryTestQueries(false).UseInMemoryTestStores(false));
                break;
            case "ReadOnly": builder.Entity<ImmediateRow>().WriteEnabled = false; break;
            default: builder.Configure(options => options.RequirePortableTestCommands(false).UseInMemoryTestCommands(false).UseInMemoryTestQueries(false).UseInMemoryTestStores(false).UseScriptedTestDoubles(null)); break;
        }
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents)).Should().NotContain("ExecuteUpdateReturningAsync<");
    }

    [Theory]
    [InlineData("Tracked")]
    [InlineData("Atomic")]
    [InlineData("Key")]
    [InlineData("DuplicateSetter")]
    [InlineData("Captured")]
    public void OptInIsNotAnEscapeHatchForUnsafeOrUnsupportedWrites(string scenario)
    {
        using var context = Context(); var builder = Builder(); var entity = builder.Entity<ImmediateRow>();
        var command = entity.HasCommand("Unsupported").SimulateImmediateWrites();
        switch (scenario)
        {
            case "Tracked": command.TrackedDelete().Stage(); break;
            case "Atomic": command.Create<ImmediateRequest>(request => new ImmediateRow { Id = request.Delta }).AllowWrite(row => row.Id).InsertIfAbsent(row => row.Id); break;
            case "Key": command.Update(setters => setters.SetProperty(row => row.Id, row => row.Id + 1)); break;
            case "DuplicateSetter": command.Update(setters => setters.SetProperty(row => row.Amount, 1).SetProperty(row => row.Amount, 2)); break;
            default:
                var external = new ImmediateRequest(1);
                command.Where(row => row.OwnerId == external.OwnerId).Delete();
                break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>();
        if (scenario is "Key" or "DuplicateSetter" or "Captured") return; // Production validation/rendering cannot be bypassed with a fixture override.
        command.RequireTestOverride("Handwritten fixture behavior is intentionally required.");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void PublicMappedImmediateWritesCompileWithoutPersistenceModelReferences()
    {
        using var context = Context(); var bundle = RepositoryGenerator.Generate(context, Builder(mapped: true).Build());
        const string types = """
            namespace RonSijm.RepoGen.Tool.Tests;
            public sealed record ImmediateData(int Key, int OwnerId, string Code, int Amount, int Previous, bool Hidden, int Revision, byte[] Marker, string Tag = "arranged");
            public sealed record ImmediateRequest(int OwnerId, string Code = "", int Delta = 1, int ExpectedVersion = 1, string Partition = "A");
            public sealed record ImmediateStampRequest(int Id, byte[] Stamp);
            public sealed record ImmediateView(int Id, string Code, int Amount, int Previous, int Version, byte[] Stamp);
            """;
        var sources = bundle.Files.Where(file => file.Kind is RepositoryOutputKind.Contracts or RepositoryOutputKind.TestDoubles)
            .Select(file => CSharpSyntaxTree.ParseText(file.Contents)).Append(CSharpSyntaxTree.ParseText(types));
        var compilation = CSharpCompilation.Create("ModelFreeImmediate", sources,
            ScriptedTestDoubleTestHarness.TestingReferences().Select(path => MetadataReference.CreateFromFile(path)), ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream(); var emitted = compilation.Emit(output);
        emitted.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
    }

    [Fact]
    public void DependentDeletesStillRequireOverrideRatherThanPretendToCascade()
    {
        using var context = new ProxyWriteGraphContext(new DbContextOptionsBuilder<ProxyWriteGraphContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = ProxyWriteTestHarness.Builder(); var entity = ProxyWriteRepositoryDesigner.Store(builder);
        entity.HasCommand("DeleteAll").Delete().SimulateImmediateWrites();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*dependent relationships*");
    }
}
