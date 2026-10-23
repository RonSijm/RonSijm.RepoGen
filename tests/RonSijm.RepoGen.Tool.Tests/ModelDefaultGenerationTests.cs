using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ModelDefaultTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelDefaultGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void ConstantsAndTypedSentinelsCompileInEveryLayout(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context(); var builder = Builder(mapped);
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, builder.Build());
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("OptedInModelConstants").And.Contain("PreserveUnlessEqualToModelSentinel");
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptInChangesNoProductionContractsOrApiAndDisabledBackendEmitsNothing(bool mapped)
    {
        using var context = Context(); var on = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var off = RepositoryGenerator.Generate(context, Builder(mapped, enabled: false, strict: false).Build());
        foreach (var file in on.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            off.Files.Single(other => other.Name == file.Name && other.Kind == file.Kind).Contents.Should().Be(file.Contents);
        on.CreateApiSnapshot().ToJson().Should().Be(off.CreateApiSnapshot().ToJson());
        RepositoryGenerator.Generate(context, Builder(mapped, backend: false).Build()).Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("insertPolicies");
        Action strict = () => RepositoryGenerator.Generate(context, Builder(mapped, enabled: false).Build()); strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*generated values*");
    }
    [Fact]
    public void DefaultsAreStoreScopedRemovableAndRequireAStore()
    {
        var builder = new RepositoryModelBuilder(); var entity = builder.Entity<ModelDefaultRow>();
        Action missing = () => entity.UseInMemoryModelDefaults(); missing.Should().Throw<InvalidOperationException>().WithMessage("RGD3501:*");
        entity.HasInMemoryStore(row => row.Id, row => new ModelDefaultRow { Id = row.Id });
        entity.UseInMemoryModelDefaults().UseInMemoryModelDefaults(false);
        builder.Build().Entities.Single().InMemoryStore!.ModelDefaultsEnabled.Should().BeFalse();
        var mapped = Builder(); mapped.Entity<ModelDefaultRow>().WithoutInMemoryEntityMapping();
        mapped.Build().Entities.Single().InMemoryStore!.ModelDefaultsEnabled.Should().BeFalse();
        entity.UseInMemoryModelDefaults(); entity.HasInMemoryStore(row => row.Id, row => new ModelDefaultRow { Id = row.Id });
        builder.Build().Entities.Single().InMemoryStore!.ModelDefaultsEnabled.Should().BeFalse();
    }
}
