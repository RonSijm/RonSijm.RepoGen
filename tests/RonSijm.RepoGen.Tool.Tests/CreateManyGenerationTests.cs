using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CreateManyGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void EveryFileLayoutCompilesWithOrderedSavedBatchResults(RepositoryOutputLayout layout)
    {
        using var context = CreateManyTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(context, new CreateManyRepositoryDesigner(mapped: true), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("OneResultPerInputInInputOrder");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EntityAndPublicDataStoresCompileInBothOutputLayouts(bool mapped, bool separate)
    {
        using var context = CreateManyTestHarness.Context();
        var design = new CreateManyRepositoryDesigner(mapped, separate: separate).CreateModel();
        var bundle = RepositoryGenerator.Generate(context, design);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        SavedResultTestHarness.CompileProduction(context, design);
        var contract = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Contracts).Contents;
        contract.Should().Contain("CommandResult<global::System.Collections.Generic.IReadOnlyList<global::System.Int32>>");
        contract.Should().NotContain("CreateManyRow>").And.NotContain("CreateManyItem item");
        var production = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Repositories).Contents;
        production.Should().Contain("TrackedCreateManyExecutor.ExecuteAsync").And.Contain("RepositoryCreateInputs.Prepare");
        using var manifest = JsonDocument.Parse(bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents);
        var command = manifest.RootElement.GetProperty("entities").EnumerateArray().Single(entity => entity.GetProperty("entity").GetString()!.EndsWith("CreateManyRow", StringComparison.Ordinal))
            .GetProperty("commands").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "FreshAsync");
        command.GetProperty("createMany").GetProperty("maximumItems").GetInt32().Should().Be(3);
        command.GetProperty("createMany").GetProperty("nativeBulkInsert").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void OptionalBackendLeavesAnExplicitScriptAndNoCreateSimulation()
    {
        using var context = CreateManyTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(context, new CreateManyRepositoryDesigner(backend: false));
        ScriptedTestDoubleTestHarness.Compile(bundle);
        var testing = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents;
        testing.Should().NotContain("CreateManyAsync<");
        testing.Should().Contain("ScriptedOperation");
    }

    [Fact]
    public void KeyedLeasesCompileAndPrepareInputsBeforeAcquiringAContext()
    {
        using var context = CreateManyTestHarness.Context();
        var design = new CreateManyRepositoryDesigner(keyed: true).CreateModel();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, design));
        SavedResultTestHarness.CompileProduction(context, design);
        var model = RepositoryModelFactory.Create(context, typeof(CreateManyContext).Assembly, design: design);
        var source = RepositorySourceRenderer.Render(model);
        var method = source[source.IndexOf("CreateAsync(", StringComparison.Ordinal)..];
        method.IndexOf("RepositoryCreateInputs.Prepare", StringComparison.Ordinal).Should().BeLessThan(method.IndexOf("Acquire", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("zero")]
    [InlineData("unbounded")]
    [InlineData("predicate")]
    [InlineData("patch")]
    [InlineData("version")]
    [InlineData("reload")]
    [InlineData("stage-result")]
    [InlineData("mixed-create")]
    [InlineData("constructor")]
    [InlineData("navigation")]
    public void UnsupportedCombinationsFailDuringGeneration(string mode)
    {
        using var context = CreateManyTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("CreateMany.Diagnostics").GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<CreateManyRow>();
        var command = entity.HasCommand("Create");
        if (mode == "constructor") command.CreateMany<CreateManyItem, CreateManyRequest>(request => request.Items, item => new CreateManyRow());
        else if (mode == "navigation") command.CreateMany<CreateManyItem, CreateManyRequest>(request => request.Items, item => new CreateManyRow { Name = item.Name, Parent = new CreateManyParent() });
        else command.CreateMany<CreateManyItem, CreateManyRequest>(request => request.Items, item => new CreateManyRow { Name = item.Name },
            maximumItems: mode == "zero" ? 0 : mode == "unbounded" ? int.MaxValue : 3);
        if (mode != "default") command.Save();
        switch (mode)
        {
            case "predicate": command.Where(row => !row.Hidden); break;
            case "patch": command.Patch(setters => setters.SetProperty(row => row.Name, "bad")); break;
            case "version": command.ExpectVersion<int, int>(row => row.Id, (row, expected) => expected); break;
            case "reload": command.ReloadAfterSave(); break;
            case "stage-result": command.Stage().ReturnsSaved(row => row.Id); break;
            case "mixed-create": command.Create<CreateManyRequest>(request => new CreateManyRow { Name = request.Label }); break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FiniteDefaultAndSimpleDefaultsKeepTheExplicitCommandWithoutGenericRangeHelpers()
    {
        using var context = CreateManyTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("CreateMany.Defaults").GenerateOnlyConfiguredEntities());
        builder.Entity<CreateManyRow>().HasCommand("Create").CreateMany<CreateManyItem, CreateManyRequest>(request => request.Items,
            item => new CreateManyRow { Name = item.Name }).Save();
        var model = RepositoryModelFactory.Create(context, typeof(CreateManyContext).Assembly, design: builder.Build());
        model.Entities.Single().Commands.Single().CreateMany!.MaximumItems.Should().Be(1000);
        ClosedRepositorySourceRenderer.RenderContracts(model).Should().Contain("CreateAsync").And.NotContain("AddRange");
    }
}
