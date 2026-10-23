using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ProjectionTranslationTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProjectionTranslationGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void FlatDtoHelpersCompileInEveryLayoutWithoutProductionHelperOrOrmCalls(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = MemoryWriteTestHarness.Context(); var design = Builder(mapped).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("new global::RonSijm.RepoGen.Tool.Tests.ProjectionNoteCard")
            .And.NotContain("global::RonSijm.RepoGen.Tool.Tests.ProjectionNoteCalls.").And.NotContain("EntityFrameworkCore");
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("ExplicitFlatDtoConstruction").And.Contain("ExactClosedMethodOverloadAndFixedCallShape").And.Contain("NotProviderEquivalence");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TranslationRegistrationCannotChangeProductionOrContractSource(bool mapped)
    {
        using var context = MemoryWriteTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var off = RepositoryGenerator.Generate(context, Builder(mapped, translations: false, strict: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            off.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        off.CreateApiSnapshot().ToJson().Should().Be(enabled.CreateApiSnapshot().ToJson());
    }

    [Fact]
    public void UnusedAndDisabledRegistrationsHaveNoOutputEffectAndNoCtorExecution()
    {
        using var context = MemoryWriteTestHarness.Context();
        var builder = Builder(queries: false, commands: false); var disabled = RepositoryGenerator.Generate(context, builder.Build());
        disabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("expressionTranslations");
        var count = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence").UseScriptedTestDoubles(Namespace + ".Testing")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        var entity = count.Entity<MemoryWriteNote>(); entity.WriteEnabled = false;
        entity.HasInMemoryStore(row => row.Id, row => new MemoryWriteNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden }).HasQuery("Count").Count();
        var baseline = RepositoryGenerator.Generate(context, count.Build());
        count.HasInMemoryProjectionTranslation<int, ProjectionNoteThrowingDto>("NeverConstruct", id => ProjectionNoteCalls.Echo<ProjectionNoteThrowingDto>(id), id => new ProjectionNoteThrowingDto(id));
        RepositoryGenerator.Generate(context, count.Build()).Files.Should().BeEquivalentTo(baseline.Files);
    }

    [Fact]
    public void SavedCommandResultsNeedTheirOwnTranslationOptIn()
    {
        using var context = MemoryWriteTestHarness.Context(); var builder = Builder(commandOptIn: false, strict: false);
        var model = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build());
        model.Entities.Single().Queries.Where(query => query.MethodName != "WrongLiteralAsync").Should().OnlyContain(query => query.InMemoryQuery!.Supported);
        model.Entities.Single().Commands.Where(command => command.MethodName != "StageRenameAsync").Should().OnlyContain(command => !command.InMemoryCommand!.Supported);
        builder.Configure(options => options.RequirePortableTestCommands());
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }
}
