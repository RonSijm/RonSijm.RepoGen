using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScriptedTestDoubleGenerationTests
{
    [Fact]
    public void DefaultOffDoesNotChangeProductionContractsManifestOrApiSnapshot()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var original = RepositoryGenerator.Generate(ScriptedTestDoubleTestHarness.Model(context, enabled: false));
        var enabled = RepositoryGenerator.Generate(ScriptedTestDoubleTestHarness.Model(context));
        original.Files.Should().NotContain(file => file.Kind == RepositoryOutputKind.TestDoubles);
        original.Model.Settings.ScriptedTestDoubleNamespace.Should().BeNull();
        enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts)
            .Should().Equal(original.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts));
        enabled.CreateApiSnapshot().ToJson().Should().Be(original.CreateApiSnapshot().ToJson());
        var manifest = JsonDocument.Parse(enabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents);
        var withoutTesting = System.Text.Json.Nodes.JsonNode.Parse(manifest.RootElement.GetRawText())!;
        withoutTesting.AsObject().Remove("testDoubles");
        withoutTesting.ToJsonString().Should().Be(System.Text.Json.Nodes.JsonNode.Parse(original.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents)!.ToJsonString());
    }

    [Theory]
    [InlineData(RepositoryGenerationProfile.Simple)]
    [InlineData(RepositoryGenerationProfile.Compatibility)]
    public void ProfilesNeverEnableTestDoubleOutputImplicitly(RepositoryGenerationProfile profile)
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseProfile(profile));
        RepositoryGenerator.Generate(context, builder.Build()).Files.Should().NotContain(file => file.Kind == RepositoryOutputKind.TestDoubles);
    }

    [Fact]
    public void EntityOptOutInheritanceAndCapabilitiesAreHonored()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var builder = ScriptedTestDoubleTestHarness.Builder();
        var row = builder.Entity<ScriptedRow>();
        row.WriteEnabled = false;
        row.HasQuery("Count").Count();
        var other = builder.Entity<ScriptedOtherRow>().GenerateTestDouble(false);
        other.ReadEnabled = false;
        other.HasCommand("Delete").Delete();
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        var source = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents;
        source.Should().Contain("OnCountAsync").And.NotContain("OnDeleteAsync").And.NotContain("ScriptedOtherRowRepositoryTestDouble");
        source.Should().Contain("IReadScriptedRowRepository").And.NotContain("IWriteScriptedRowRepository");
        ScriptedTestDoubleTestHarness.Compile(bundle);
        other.GenerateTestDouble();
        RepositoryGenerator.Generate(context, builder.Build()).Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents
            .Should().Contain("ScriptedOtherRowRepositoryTestDouble");
        other.GenerateTestDouble(false).GenerateTestDouble(null);
        RepositoryGenerator.Generate(context, builder.Build()).Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents
            .Should().Contain("ScriptedOtherRowRepositoryTestDouble");
        row.ReadEnabled = false;
        other.WriteEnabled = false;
        RepositoryGenerator.Generate(context, builder.Build()).Files.Should().NotContain(file => file.Kind == RepositoryOutputKind.TestDoubles);
    }

    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, 1)]
    [InlineData(RepositoryOutputLayout.PerEntity, 2)]
    [InlineData(RepositoryOutputLayout.PerContract, 2)]
    public async Task ExactOverloadsAndBoundPortsCompileAndExecuteWithoutAContext(RepositoryOutputLayout layout, int count)
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(ScriptedTestDoubleTestHarness.Model(context), layout);
        bundle.Files.Count(file => file.Kind == RepositoryOutputKind.TestDoubles).Should().Be(count);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("OnFindAsyncByInt32").And.Contain("OnFindAsyncByString").And.Contain("Identifier { get; }")
            .And.Contain("global::System.String? Text { get; }").And.NotContain("OnExistsAsync");
        source.Should().NotContain("DbContext").And.NotContain("EntityFramework").And.NotContain("IQueryable");
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle);
        var fixture = ScriptedTestDoubleTestHarness.Fixture(assembly);
        var read = (IScriptedReadPort)fixture;
        var write = (IScriptedWritePort)fixture;
        var byId = new ScriptedView(1, "By ID");
        var byText = new ScriptedView(2, "By text");
        ScriptedTestDoubleTestHarness.Returns(fixture, "OnFindAsyncByInt32", byId);
        ScriptedTestDoubleTestHarness.Returns(fixture, "OnFindAsyncByString", byText);
        ScriptedTestDoubleTestHarness.Returns(fixture, "OnOptionalLookupAsync", null);
        ScriptedTestDoubleTestHarness.Returns(fixture, "OnDeleteAsync", 3);
        (await read.FindAsync()).Should().BeSameAs(byId);
        (await read.FindAsync(text: "x")).Should().BeSameAs(byText);
        (await read.OptionalLookupAsync()).Should().BeNull();
        (await read.ExistsAsync(1)).Should().BeTrue("default interface methods keep their handwritten behavior");
        (await write.DeleteAsync(9)).Should().Be(3);
        assembly.GetReferencedAssemblies().Should().NotContain(name => name.Name!.Contains("EntityFramework", StringComparison.Ordinal));
    }

    [Fact]
    public void ScopedRegistrationReplacesProductionAliasesAndSharesOneFixtureForAllPorts()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(ScriptedTestDoubleTestHarness.Model(context)));
        var services = new ServiceCollection();
        services.AddSingleton<IScriptedReadPort>(_ => throw new InvalidOperationException("production must not be selected"));
        var register = assembly.GetType("Generated.Scripted.TestDoubles.ScriptedContextRepositoryTestDoubleRegistration")!.GetMethod("AddScriptedContextRepositoryTestDoubles")!;
        register.Invoke(null, [services]);
        register.Invoke(null, [services]);
        services.Count(descriptor => descriptor.ServiceType == typeof(IScriptedReadPort)).Should().Be(1);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var fixture = scope.ServiceProvider.GetRequiredService(assembly.GetType("Generated.Scripted.TestDoubles.IScriptedRowRepositoryTestDouble")!);
        scope.ServiceProvider.GetRequiredService<IScriptedReadPort>().Should().BeSameAs(fixture);
        scope.ServiceProvider.GetRequiredService<IScriptedWritePort>().Should().BeSameAs(fixture);
        foreach (var name in new[] { "IReadScriptedRowRepository", "IWriteScriptedRowRepository", "IScriptedRowRepository" })
            scope.ServiceProvider.GetRequiredService(assembly.GetType("Generated.Scripted.Contracts." + name)!).Should().BeSameAs(fixture);
        using var otherScope = provider.CreateScope();
        otherScope.ServiceProvider.GetRequiredService<IScriptedReadPort>().Should().NotBeSameAs(fixture);
    }

    [Fact]
    public async Task TypedGeneratedScriptsHandleRecordingQueuesStreamsAndFixtureReset()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        const string consumer = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Generated.Scripted.TestDoubles;
            using RonSijm.RepoGen.Tool.Tests;
            public static class ScriptedConsumer
            {
                public static async Task<bool> Exercise()
                {
                    var fixture = new ScriptedRowRepositoryTestDouble();
                    fixture.OnFindAsyncByInt32.RecordCalls(args => new(args.Identifier));
                    fixture.OnFindAsyncByInt32.EnqueueResult(new ScriptedView(2, "queued"), args => args.Identifier == 2);
                    fixture.OnFindAsyncByInt32.Returns(null);
                    IScriptedReadPort read = fixture;
                    var result = await read.FindAsync(identifier: 2);
                    if (result?.Name != "queued" || fixture.OnFindAsyncByInt32.Calls[0].Arguments.Identifier != 2) return false;
                    if (await read.FindAsync(2) is not null) return false;
                    fixture.OnStreamAsync.ReturnsItems(new[] { new ScriptedView(3, "stream") });
                    var items = new List<ScriptedView>();
                    await foreach (var item in read.StreamAsync()) items.Add(item);
                    fixture.ClearCalls();
                    if (fixture.OnFindAsyncByInt32.InvocationCount != 0 || fixture.OnStreamAsync.InvocationCount != 0) return false;
                    if (await read.FindAsync(2) is not null) return false;
                    fixture.ResetTestDouble();
                    try { await read.FindAsync(2); return false; }
                    catch (RonSijm.RepoGen.Testing.UnarrangedRepositoryOperationException) { return items.Count == 1; }
                }
            }
            """;
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(ScriptedTestDoubleTestHarness.Model(context)), consumer);
        (await (Task<bool>)assembly.GetType("ScriptedConsumer")!.GetMethod("Exercise")!.Invoke(null, null)!).Should().BeTrue();
    }

    [Fact]
    public async Task UnarrangedGeneratedWritesFailRatherThanSucceedSilently()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var fixture = ScriptedTestDoubleTestHarness.Fixture(ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(ScriptedTestDoubleTestHarness.Model(context))));
        Func<Task> delete = () => ((IScriptedWritePort)fixture).DeleteAsync(1);
        await delete.Should().ThrowAsync<UnarrangedRepositoryOperationException>().WithMessage("*DeleteAsync*");
    }

    [Fact]
    public void ManifestExplicitlyClassifiesAllOperationsAsScriptedOverrides()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(ScriptedTestDoubleTestHarness.Model(context));
        using var manifest = JsonDocument.Parse(bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents);
        var testing = manifest.RootElement.GetProperty("testDoubles");
        testing.GetProperty("mode").GetString().Should().Be("Scripted");
        testing.GetProperty("executesDesignerPipeline").GetBoolean().Should().BeFalse();
        var operations = testing.GetProperty("repositories")[0].GetProperty("operations").EnumerateArray().ToArray();
        operations.Should().HaveCount(6);
        operations.Should().OnlyContain(operation => operation.GetProperty("status").GetString() == "RequiresOverride");
        operations.Count(operation => operation.GetProperty("stream").GetBoolean()).Should().Be(1);
        operations.Should().NotContain(operation => operation.GetProperty("signature").GetString()!.StartsWith("ExistsAsync", StringComparison.Ordinal));
        bundle.Files.Should().Equal(RepositoryGenerator.Generate(ScriptedTestDoubleTestHarness.Model(context)).Files);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidPrerequisitesFailWithStableDiagnostic(bool entityOnly)
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var builder = new RepositoryModelBuilder();
        if (entityOnly) builder.Entity<ScriptedRow>().GenerateTestDouble();
        else builder.Configure(options => options.UseScriptedTestDoubles("Generated.TestDoubles"));
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3101:*");
    }

    [Fact]
    public void ExplicitNullDisablesTestGenerationAndBlankNamespaceIsRejected()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var builder = ScriptedTestDoubleTestHarness.Builder().Configure(options => options.UseScriptedTestDoubles(null));
        builder.Entity<ScriptedRow>().HasQuery("Count").Count();
        RepositoryGenerator.Generate(context, builder.Build()).Files.Should().NotContain(file => file.Kind == RepositoryOutputKind.TestDoubles);
        Action blank = () => builder.Configure(options => options.UseScriptedTestDoubles(" "));
        blank.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ConflictingFixtureControlsOnDefaultPortMethodsFailEarly()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var builder = ScriptedTestDoubleTestHarness.Builder();
        builder.Entity<ScriptedRow>().BindReadPort<IConflictingScriptedPort>().HasQuery("Count").Count();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3102:*");
    }

    [Fact]
    public void OptionalTestingRuntimeIsNotReferencedByTheGenerationEngine()
    {
        typeof(RepositoryGenerator).Assembly.GetReferencedAssemblies().Should().NotContain(name => name.Name == "RonSijm.RepoGen.Testing");
    }
}
