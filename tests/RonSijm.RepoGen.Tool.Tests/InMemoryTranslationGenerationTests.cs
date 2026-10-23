using System.Text.Json;
using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryTranslationTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryTranslationGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    public void TranslationsCompileInEveryLayoutWithoutAnEfOrHelperDependency(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = InMemoryQueryTestHarness.Context(); var design = Builder(mapped).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        assembly.GetReferencedAssemblies().Should().NotContain(name => name.Name!.Contains("EntityFramework", StringComparison.Ordinal));
        var fixture = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        fixture.Should().Contain("InMemoryStringPatterns.Like").And.NotContain("global::Microsoft.EntityFrameworkCore").And.NotContain("global::RonSijm.RepoGen.Tool.Tests.TranslationCalls.");
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("expressionTranslations").And.Contain("ExactClosedMethodOverloadAndFixedCallShape").And.Contain("NotProviderEquivalence");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegisteringTranslationsCannotChangeProductionApiOrCacheFingerprints(bool mapped)
    {
        using var context = InMemoryQueryTestHarness.Context();
        var translatedDesign = Builder(mapped).Build(); var originalDesign = Builder(mapped, translations: false, strict: false).Build();
        var translated = RepositoryGenerator.Generate(context, translatedDesign); var original = RepositoryGenerator.Generate(context, originalDesign);
        foreach (var file in translated.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles))
            original.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        translated.CreateApiSnapshot().ToJson().Should().Be(original.CreateApiSnapshot().ToJson());
        var translatedModel = RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: translatedDesign);
        var originalModel = RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: originalDesign);
        JsonSerializer.Serialize(translatedModel.Entities.Single().Queries).Should().Be(JsonSerializer.Serialize(originalModel.Entities.Single().Queries));
        originalModel.Entities.Single().Queries.Should().OnlyContain(query => query.InMemoryQuery != null && !query.InMemoryQuery.Supported);
    }

    [Fact]
    public void UnusedRegistrationsAndDisabledReadBackendsLeaveOutputUnchanged()
    {
        using var context = InMemoryQueryTestHarness.Context();
        var builder = InMemoryQueryTestHarness.Builder(); InMemoryQueryTestHarness.Entity(builder).HasQuery("Count").Count();
        var original = RepositoryGenerator.Generate(context, builder.Build()); Register(builder);
        RepositoryGenerator.Generate(context, builder.Build()).Files.Select(file => file.Contents).Should().Equal(original.Files.Select(file => file.Contents));
        var disabled = RepositoryGenerator.Generate(context, Builder(enabled: false).Build());
        disabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("expressionTranslations");
    }

    [Fact]
    public void ClosedGenericOverloadsAndFixedLiteralsDoNotActAsWildcards()
    {
        using var context = InMemoryQueryTestHarness.Context(); var builder = Builder(strict: false);
        builder.Entity<PortableRow>().HasQuery("OtherGeneric").Select(row => TranslationCalls.Echo(row.Name)).List();
        var model = RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: builder.Build());
        model.Entities.Single().Queries.Single(query => query.MethodName == "OtherGenericAsync").InMemoryQuery!.Supported.Should().BeFalse();
        model.Entities.Single().Queries.Single(query => query.MethodName == "WrongFixedAsync").InMemoryQuery!.Supported.Should().BeFalse();
        var used = model.Entities.Single().Queries.Single(query => query.MethodName == "NestedAsync").InMemoryQuery!.Translations!;
        used.Select(translation => translation.Name).Should().Contain("BooleanEcho").And.Contain("OrdinalIgnoreCasePatterns").And.NotContain("FixedUnit");
    }
}
