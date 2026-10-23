using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.FilteredCollectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredCollectionGuardTests
{
    [Theory]
    [InlineData("context", true)]
    [InlineData("context", false)]
    [InlineData("capture", true)]
    [InlineData("capture", false)]
    [InlineData("clock", true)]
    [InlineData("clock", false)]
    [InlineData("navigation", true)]
    [InlineData("navigation", false)]
    [InlineData("helper", true)]
    [InlineData("helper", false)]
    [InlineData("autoinclude", true)]
    [InlineData("autoinclude", false)]
    public void ExplicitCollectionPermissionDoesNotExecuteOrPermitUnsupportedModelBehavior(string mode, bool mapped)
    {
        using var context = GuardContext(mode); var builder = Builder(mapped);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScalarHelperRecipesStillNeedSeparateCommandTranslationConsent(bool mapped)
    {
        using var context = GuardContext("helper"); var disabled = Builder(mapped);
        disabled.HasInMemoryTranslation<string, bool>("KeepText", text => FilteredCollectionHelpers.Keep(text), text => text != "deleted");
        Action generate = () => RepositoryGenerator.Generate(context, disabled.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        var enabled = Builder(mapped, translations: true);
        enabled.HasInMemoryTranslation<string, bool>("KeepText", text => FilteredCollectionHelpers.Keep(text), text => text != "deleted");
        var bundle = RepositoryGenerator.Generate(context, enabled.Build());
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("FilteredCollectionHelpers");
        var (parent, _, data) = Fixture(mapped, assembly, nameof(FilteredCollectionGuardContext));
        (await Invoke<Task<CommandResult<MemoryCollectionView>>>(parent, "SaveItems", new MemoryCollectionRequest(1, 10, "changed", []))).Succeeded.Should().BeTrue();
        Rows(data, mapped).Select(row => row.Id).Should().Equal(3, 4, 5, 6);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WritePermissionsDoNotDependOnReadFlagsOrEnableFreshFilteredGraphs(bool saved)
    {
        using var context = Context(); var builder = Builder();
        builder.Entity<MemoryCollectionChild>().UseInMemoryModelQueryFilter(false);
        if (!saved) builder.Entity<MemoryCollectionParent>().ExcludeCommand("SavedItems");
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        if (saved) generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Active EF model query filters*");
        else ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void UnmappedFilterMembersAndMissingWriteMapsAreNotSilentlyIgnored()
    {
        using var context = Context(); var builder = Builder();
        builder.Entity<MemoryCollectionChild>().MapInMemoryEntity<FilteredCollectionChildSeed>(row => new MemoryCollectionChild
            { Id = row.Id, ParentId = row.ParentId, Key = row.Key, Version = row.Version, Hidden = row.Hidden });
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParameterlessOrDisabledSimulationClearsTheAdditionalFilterPermission(bool disable)
    {
        using var context = Context(); var builder = Builder(strict: false); var parent = builder.Entity<MemoryCollectionParent>();
        parent.ExcludeCommand("SaveItems");
        var command = parent.HasCommand("SaveItems").Where(row => row.Id == 1)
            .ReconcileCollection<MemoryCollectionChild, MemoryCollectionItem, string, MemoryCollectionRequest>(row => row.Children, input => input.Items!, row => row.Key, item => item.Key,
                collection => collection.MaximumItems(3).MissingItems(RepositoryCollectionRemovalBehavior.Delete))
            .UseInMemoryModelQueryFilter().SimulateCollectionWrites(includeModelQueryFilters: true).Save();
        command.SimulateCollectionWrites(!disable);
        builder.Configure(options => options.RequirePortableTestCommands());
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChildOnlyContextBindingsUseTheResolvedKeyOnceAndPreservePartitionIsolation(bool mapped)
    {
        using var context = new FilteredCollectionKeyContext(new DbContextOptionsBuilder<MemoryCollectionContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder(mapped); var parent = builder.Entity<MemoryCollectionParent>();
        foreach (var command in builder.Build().Entities.Single(entity => entity.EntityType == typeof(MemoryCollectionParent)).Commands.ToArray())
            parent.ExcludeCommand(command.MethodName);
        builder.Entity<MemoryCollectionChild>().ExcludeCommand("StageText");
        builder.Configure(options => options.UseKeyedContextLeases<ContextFilterKey>());
        builder.HasInMemoryModelFilterContext<FilteredCollectionKeyContext, ContextFilterKey, int>(ctx => ctx.Partition, key => key.FolderId);
        parent.HasCommand("Apply").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key)
            .Where<ContextFilterRequest>((row, input) => row.Id == input.Id)
            .ReconcileCollection<MemoryCollectionChild, MemoryCollectionItem, string, ContextFilterRequest>(row => row.Children, input => Array.Empty<MemoryCollectionItem>(), row => row.Key, item => item.Key,
                collection => collection.MaximumItems(3).MissingItems(RepositoryCollectionRemovalBehavior.Delete))
            .UseInMemoryModelQueryFilter().SimulateCollectionWrites(includeModelQueryFilters: true).Save();
        var bundle = RepositoryGenerator.Generate(context, builder.Build()); var assembly = ScriptedTestDoubleTestHarness.Compile(bundle);
        SavedResultTestHarness.CompileProduction(context, builder.Build());
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.FilteredCollectionKeyContextRepositoryFixture")!)!;
        var firstKey = new ContextFilterKey("A", FolderId: 1); var secondKey = new ContextFilterKey("B", FolderId: 2);
        var data = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, [firstKey])!; Seed(data, mapped);
        var second = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, [secondKey])!; Seed(second, mapped);
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryCollectionParentRepositoryTestDouble")!, fixture)!;
        var input = new ContextFilterRequest(firstKey, 1);
        (await Invoke<Task<CommandResult>>(repository, "Apply", input)).Succeeded.Should().BeTrue(); input.KeyReads.Should().Be(1);
        Rows(data, mapped).Select(row => row.Id).Should().Equal(3, 4, 5, 6);
        var other = new ContextFilterRequest(secondKey, 1);
        (await Invoke<Task<CommandResult>>(repository, "Apply", other)).Status.Should().Be(CommandStatus.NoChanges); other.KeyReads.Should().Be(1);
        Rows(second, mapped).Should().HaveCount(6);
    }

    private static FilteredCollectionGuardContext GuardContext(string mode) => new(new DbContextOptionsBuilder<MemoryCollectionContext>()
        .UseSqlite("Data Source=:memory:").ReplaceService<IModelCacheKeyFactory, FilteredCollectionModelCacheKeyFactory>().Options, mode);
}
