using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryWriteGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void WriteOptionDoesNotChangeProductionContractsOrApiAndEveryLayoutCompiles(RepositoryOutputLayout layout)
    {
        using var context = MemoryWriteTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, new MemoryWriteRepositoryDesigner(), layout);
        var disabled = RepositoryGenerator.Generate(context, new MemoryWriteRepositoryDesigner(false), layout);
        enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.TestDoubles and not RepositoryOutputKind.Manifest)
            .Select(file => file.Contents).Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.TestDoubles and not RepositoryOutputKind.Manifest)
                .Select(file => file.Contents));
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        ScriptedTestDoubleTestHarness.Compile(enabled);
        var manifest = enabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("writeSimulation").And.Contain("PendingAssignmentsSeparateFromStoreRows");
        disabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("writeSimulation");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommandSettingsHaveExplicitPrerequisites(bool strict)
    {
        using var context = MemoryWriteTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => { if (strict) options.RequirePortableTestCommands(); else options.UseInMemoryTestCommands(); });
        Action invalid = () => RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: builder.Build());
        invalid.Should().Throw<InvalidOperationException>().WithMessage("RGD3401:*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StrictCoverageRequiresAcknowledgementOfNativeWrites(bool acknowledge)
    {
        using var context = MemoryWriteTestHarness.Context();
        var builder = Builder();
        var command = builder.Entity<MemoryWriteNote>().HasCommand("Native").Where<int>((row, id) => row.Id == id).Delete();
        if (acknowledge) command.RequireTestOverride("Native DELETE belongs in the provider contract suite.");
        if (!acknowledge)
        {
            Action invalid = () => RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: builder.Build());
            invalid.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Native*");
        }
        else
        {
            var bundle = RepositoryGenerator.Generate(context, builder.Build());
            bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("RequiresOverride").And.Contain("Native DELETE belongs");
            ScriptedTestDoubleTestHarness.Compile(bundle);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedKeysAndReloadRemainExplicitBoundaries(bool generated)
    {
        using var context = generated ? new MemoryGeneratedWriteContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<MemoryWriteContext>().UseSqlite("Data Source=:memory:").Options) : MemoryWriteTestHarness.Context();
        var builder = Builder();
        if (!generated) builder.Entity<MemoryWriteNote>().HasCommand("Reload").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(row => row.Title, row => "next")).Save().ReloadAfterSave();
        Action invalid = () => RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: builder.Build());
        invalid.Should().Throw<InvalidOperationException>().WithMessage(generated ? "RGD3402:*generated values*" : "RGD3402:*ReloadAfterSave*");
    }

    [Fact]
    public void AutoIncludedManyToManyStateNeedsAnExplicitOverrideEvenWithoutOrdinaryNavigations()
    {
        using var context = new MemorySkipWriteContext(new DbContextOptionsBuilder<MemorySkipWriteContext>().UseSqlite("Data Source=:memory:").Options);
        var entity = context.Model.FindEntityType(typeof(NavigationBoundaryOwner))!;
        entity.GetNavigations().Should().BeEmpty();
        entity.GetSkipNavigations().Should().ContainSingle(navigation => navigation.IsEagerLoaded);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.SkipWrites.Contracts")
            .UseScriptedTestDoubles("Generated.SkipWrites.TestDoubles").UseInMemoryTestStores().UseInMemoryTestCommands()
            .RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        var owner = builder.Entity<NavigationBoundaryOwner>();
        owner.HasInMemoryStore(row => row.Id, row => new NavigationBoundaryOwner { Id = row.Id, Code = row.Code });
        owner.HasCommand("Rename").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Code, row => "next")).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*AutoInclude*");
    }

    [Fact]
    public void CommandTestingModelsDoNotChangeCacheOperationShapes()
    {
        using var context = MemoryWriteTestHarness.Context();
        var enabled = RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: new MemoryWriteRepositoryDesigner().CreateModel());
        var disabled = RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: new MemoryWriteRepositoryDesigner(false).CreateModel());
        System.Text.Json.JsonSerializer.Serialize(enabled.Entities.SelectMany(entity => entity.Commands)).Should()
            .Be(System.Text.Json.JsonSerializer.Serialize(disabled.Entities.SelectMany(entity => entity.Commands)));
    }

    [Theory]
    [InlineData("left", false)]
    [InlineData("right", false)]
    [InlineData("assigned", false)]
    [InlineData("source", true)]
    [InlineData("target", false)]
    [InlineData("property", false)]
    public void WriteHelpersRespectValidApplicationNamesAndExistingInfrastructureRestrictions(string parameterName, bool reserved)
    {
        using var context = MemoryWriteTestHarness.Context();
        var builder = Builder();
        var entity = System.Linq.Expressions.Expression.Parameter(typeof(MemoryWriteNote), "row");
        var request = System.Linq.Expressions.Expression.Parameter(typeof(MemoryWriteRequest), parameterName);
        var value = System.Linq.Expressions.Expression.Lambda<Func<MemoryWriteNote, MemoryWriteRequest, string>>(
            System.Linq.Expressions.Expression.Property(request, nameof(MemoryWriteRequest.Title)), entity, request);
        builder.Entity<MemoryWriteNote>().HasCommand("Names").Where(row => row.Id == 1)
            .Patch(setters => setters.SetProperty<string, MemoryWriteRequest>(row => row.Title, value)).Save();
        if (reserved)
        {
            // The production renderer already reserves source; the optional fixture backend must not weaken that guard.
            Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
            generate.Should().Throw<InvalidOperationException>().WithMessage("Operation parameter 'source' conflicts with generated infrastructure.*");
        }
        else ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void ExcludingTestDoubleIgnoresCommandCoverageButPreservesProductionOperations()
    {
        using var context = MemoryWriteTestHarness.Context();
        var builder = Builder();
        var note = builder.Entity<MemoryWriteNote>();
        note.GenerateTestDouble(false);
        note.HasCommand("Native").Where<int>((row, id) => row.Id == id).Delete();
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("MemoryWriteNoteRepositoryTestDouble");
    }

    internal static RepositoryModelBuilder Builder()
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.MemoryWrites.Contracts")
            .UseGeneratedNamespace("Generated.MemoryWrites.Persistence").UseScriptedTestDoubles("Generated.MemoryWrites.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        MemoryWriteRepositoryDesigner.Configure(builder);
        return builder;
    }
}
