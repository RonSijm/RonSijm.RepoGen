using System.Text.Json;
using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryCommandTranslationTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryCommandTranslationGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    public void PerCommandTranslationsCompileAcrossLayoutsAndStoreShapes(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = MemoryWriteTestHarness.Context(); var design = Builder(mapped).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        assembly.GetReferencedAssemblies().Should().NotContain(reference => reference.Name!.Contains("EntityFramework", StringComparison.Ordinal));
        var output = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        output.Contains("InMemoryStringPatterns.Like", StringComparison.Ordinal).Should().BeTrue();
        output.Contains("global::Microsoft.EntityFrameworkCore", StringComparison.Ordinal).Should().BeFalse();
        output.Contains("global::RonSijm.RepoGen.Tool.Tests.TranslationCalls.", StringComparison.Ordinal).Should().BeFalse();
        output.Contains("global::RonSijm.RepoGen.Tool.Tests.TranslatedWriteCalls.", StringComparison.Ordinal).Should().BeFalse();
        var model = RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: design);
        var commands = model.Entities.Single().Commands;
        commands.Where(command => command.MethodName != "UnknownAsync").Should().OnlyContain(command => command.InMemoryCommand!.Supported && command.InMemoryCommand.Translations!.Count > 0 && command.InMemoryCommand.Rendering != null);
        commands.Single(command => command.MethodName == "UnknownAsync").InMemoryCommand!.Supported.Should().BeFalse();
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("NormalizedText").And.Contain("ExactClosedMethodOverloadAndFixedCallShape");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionExpressionsApiAndCacheFingerprintsCannotChange(bool mapped)
    {
        using var context = MemoryWriteTestHarness.Context();
        var design = Builder(mapped).Build(); var disabled = Builder(mapped, optIn: false, strict: false).Build();
        var translated = RepositoryGenerator.Generate(context, design); var original = RepositoryGenerator.Generate(context, disabled);
        foreach (var file in translated.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles))
            original.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        translated.CreateApiSnapshot().ToJson().Should().Be(original.CreateApiSnapshot().ToJson());
        var model = RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: design);
        var baseline = RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: disabled);
        JsonSerializer.Serialize(model.Entities.Single().Commands).Should().Be(JsonSerializer.Serialize(baseline.Entities.Single().Commands));
        baseline.Entities.Single().Commands.Should().OnlyContain(command => !command.InMemoryCommand!.Supported && command.InMemoryCommand.Translations == null);
        baseline.Entities.Single().Queries.Should().OnlyContain(query => query.InMemoryQuery!.Supported);
    }

    [Fact]
    public void PerCommandOptInAndBackendSwitchesRemainIndependentOfReads()
    {
        using var context = MemoryWriteTestHarness.Context();
        var defaults = new RepositoryModelBuilder();
        var defaultCommand = defaults.Entity<MemoryWriteNote>().HasCommand("Default").TrackedDelete().Stage();
        defaults.Build().Entities.Single().Commands.Single().InMemoryTranslations.Should().BeFalse();
        defaultCommand.UseInMemoryTranslations().UseInMemoryTranslations(false);
        defaults.Build().Entities.Single().Commands.Single().InMemoryTranslations.Should().BeFalse();
        var model = RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: Builder(queries: false).Build());
        model.Entities.Single().Commands.Single(command => command.MethodName == "SavedRenameAsync").InMemoryCommand!.Supported.Should().BeTrue();
        var noBackend = RepositoryGenerator.Generate(context, Builder(backend: false).Build());
        // The query still has a translation receipt, but none of the commands has an automatic backend.
        var backendModel = RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: Builder(backend: false).Build());
        backendModel.Entities.Single().Commands.Should().OnlyContain(command => command.InMemoryCommand == null);
        ScriptedTestDoubleTestHarness.Compile(noBackend);
        Action missing = () => RepositoryGenerator.Generate(context, Builder(translations: false).Build());
        missing.Should().Throw<InvalidOperationException>().WithMessage("*RGD3402*RequireTestOverride*");
    }
}
