using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AtomicFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicFilterGuardTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void AtomicSimulationAndCommandFilterConsentRemainIndependent(bool mapped, bool missingSimulation)
    {
        using var context = ModelWriteFilterTestHarness.Context();
        Action generate = () => RepositoryGenerator.Generate(context, Builder(mapped, filter: missingSimulation, simulate: !missingSimulation).Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Theory]
    [InlineData("context")]
    [InlineData("capture")]
    [InlineData("clock")]
    [InlineData("helper")]
    [InlineData("shadow")]
    public void NonportableModelFiltersOrProductionShapesRemainUnsupported(string mode)
    {
        using var context = GuardContext(mode); var builder = Base(mapped: false, queries: false);
        var command = builder.Entity<ModelWriteFilterNote>().HasCommand("Put").Create(Create).AllowWrite(row => row.Id).AllowWrite(row => row.Version)
            .InsertOrUpdate(row => row.Id, setters => setters.SetProperty<string, AtomicFilterInput>(row => row.Title, (row, request) => request.Title))
            .SimulateImmediateWrites().UseInMemoryModelQueryFilter();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        if (mode == "shadow") { generate.Should().Throw<InvalidOperationException>().WithMessage("*shadow*"); return; }
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        command.RequireTestOverride("The provider/context expression requires an explicit test script.");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public async Task ScalarHelperTranslationsRequireCommandPermissionAndCheckUpdatedValuesToo()
    {
        using var context = GuardContext("helper"); var builder = Base(queries: false);
        var command = builder.Entity<ModelWriteFilterNote>().HasCommand("Put").Create(Create).AllowWrite(row => row.Id).AllowWrite(row => row.Version)
            .InsertOrUpdate(row => row.Id, setters => setters.SetProperty<string, AtomicFilterInput>(row => row.Title, (row, request) => request.Title))
            .SimulateImmediateWrites().UseInMemoryModelQueryFilter();
        builder.HasInMemoryTranslation<string, bool>("AcceptTitle", title => ModelFilterGuardCalls.Accept(title), title => title != "deleted");
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        command.UseInMemoryTranslations(); var bundle = RepositoryGenerator.Generate(context, builder.Build());
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("ModelFilterGuardCalls.Accept");
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle);
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.NativeFilterGuardContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!; Seed(data, true);
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelWriteFilterNoteRepositoryTestDouble")!, fixture)!;
        Func<Task> execute = async () => await Call<CommandResult>(repository, "Put", new AtomicFilterInput(1, "deleted"));
        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("*filtered result read*");
        ModelWriteFilterTestHarness.Store<ModelWriteFilterSeed>(data).Find(1)!.Title.Should().Be("one");
    }

    [Fact]
    public async Task ExplicitContextBindingsAlsoConstrainUpdateSelectionAndFreshAtomicResults()
    {
        using var context = GuardContext("context"); var builder = Base(queries: false, keyed: true);
        builder.HasInMemoryModelFilterContext<NativeFilterGuardContext, string, string>(value => value.Tenant, key => key);
        builder.Entity<ModelWriteFilterNote>().HasCommand("Put").Create(Create).AllowWrite(row => row.Id).AllowWrite(row => row.Version)
            .InsertOrUpdate(row => row.Id, setters => setters.SetProperty<string, AtomicFilterInput>(row => row.Title, (row, request) => request.Title))
            .UseContextKey<AtomicFilterInput, string>(request => request.Partition).SimulateImmediateWrites().UseInMemoryModelQueryFilter();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.NativeFilterGuardContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, ["one"])!; Seed(data, true);
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelWriteFilterNoteRepositoryTestDouble")!, fixture)!;
        (await Call<CommandResult>(repository, "Put", new AtomicFilterInput(4, "one", Partition: "one"))).Status.Should().Be(CommandStatus.NotMatched);
        Func<Task> execute = async () => await Call<CommandResult>(repository, "Put", new AtomicFilterInput(1, "changed", Partition: "one"));
        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("*filtered result read*");
        ModelWriteFilterTestHarness.Store<ModelWriteFilterSeed>(data).Find(1)!.Title.Should().Be("one");
        (await Call<CommandResult>(repository, "Put", new AtomicFilterInput(10, "not one", Partition: "one"))).Status.Should().Be(CommandStatus.Inserted);
    }

    [Fact]
    public async Task AcknowledgedOrUnconfiguredAtomicCallsRequireScriptsEvenOnEmptyStores()
    {
        using var context = ModelWriteFilterTestHarness.Context(); var builder = Base(queries: false);
        builder.Entity<ModelWriteFilterNote>().HasCommand("Ensure").Create(Create).AllowWrite(row => row.Id).AllowWrite(row => row.Version)
            .InsertIfAbsent(row => row.Id).SimulateImmediateWrites().RequireTestOverride("Unconfigured model filter needs a script.");
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var (repository, data, _) = Fixture(assembly: assembly); data.Clear();
        Func<Task> execute = async () => await Call<CommandResult>(repository, "Ensure", new AtomicFilterInput(10));
        await execute.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        var script = repository.GetType().GetProperty("OnEnsureAsync")!.GetValue(repository)!;
        script.GetType().GetMethod("Returns")!.Invoke(script, [new CommandResult(CommandStatus.AlreadyExists)]);
        (await Call<CommandResult>(repository, "Ensure", new AtomicFilterInput(10))).Status.Should().Be(CommandStatus.AlreadyExists);
    }

    private static NativeFilterGuardContext GuardContext(string mode) => new(new DbContextOptionsBuilder<NativeFilterGuardContext>().UseSqlite("Data Source=:memory:")
        .ReplaceService<IModelCacheKeyFactory, NativeFilterCacheKeyFactory>().Options, mode);
}
