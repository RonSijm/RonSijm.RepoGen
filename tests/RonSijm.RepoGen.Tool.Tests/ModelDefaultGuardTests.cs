using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ModelDefaultTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelDefaultGuardTests
{
    [Theory]
    [InlineData("sql")]
    [InlineData("computed")]
    [InlineData("converter")]
    [InlineData("comparer")]
    [InlineData("key")]
    public void UnsupportedModelGenerationStillNeedsAnExplicitAdapter(string mode)
    {
        using var context = Context(mode);
        var model = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: GuardBuilder(mode, strict: false).Build());
        model.Entities.Single().Commands.Single(command => command.MethodName == "CreateAsync").InMemoryCommand!.Supported.Should().BeFalse();
        var values = model.Entities.Single().InMemoryStore!.GeneratedValues!;
        values.Should().NotContain(value => value.PropertyName == (mode == "key" ? "Id" : "Credits"));
        Action strict = () => RepositoryGenerator.Generate(context, GuardBuilder(mode, strict: true).Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }
    [Theory]
    [InlineData("converter")]
    [InlineData("comparer")]
    public void ExplicitFactoryDoesNotWeakenCustomSentinelComparisonBoundaries(string mode)
    {
        using var context = Context(mode);
        Action generate = () => RepositoryGenerator.Generate(context, Builder(manual: true, strict: false).Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3501:*sentinels*");
    }
    [Fact]
    public void DatabaseConstraintWithoutInsertGenerationDoesNotBecomeAFixtureDefault()
    {
        using var context = Context("never");
        var entity = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: Builder().Build()).Entities.Single();
        entity.InMemoryStore!.GeneratedValues.Should().NotContain(value => value.PropertyName == "Credits");
        entity.Commands.Single(command => command.MethodName == "CreateAsync").InMemoryCommand!.Supported.Should().BeTrue();
    }
    [Fact]
    public void MappedCreationCannotInferCustomSentinelFieldInitializers()
    {
        using var context = ModelDefaultTestHarness.Context(); var builder = Builder(strict: false);
        builder.Entity<ModelDefaultRow>().HasCommand("Implicit").Create<ModelDefaultData>(request => new ModelDefaultRow { Id = request.Id })
            .AllowWrite(row => row.Id).Save();
        var command = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build()).Entities.Single().Commands
            .Single(command => command.MethodName == "ImplicitAsync");
        command.InMemoryCommand!.Supported.Should().BeFalse();
        command.InMemoryCommand.Reasons.Should().Contain(reason => reason.Contains("custom-sentinel property", StringComparison.Ordinal));
    }
    [Fact]
    public async Task ExplicitFactoryOrderPrecedesModelConstantsAndRemovingWriteMappingClearsOptIn()
    {
        var builder = Builder(); var entity = builder.Entity<ModelDefaultRow>();
        entity.HasInMemoryGeneratedValue(row => row.Credits, (row, existing) => 20);
        entity.HasInMemoryGeneratedValue(row => row.Score, (row, existing) => row.Credits + 1);
        var (repository, _) = Fixture(true, builder);
        var value = (await Call<CommandResult<ModelDefaultData>>(repository, "Create", new ModelDefaultData(1))).Value!;
        value.Credits.Should().Be(20); value.Score.Should().Be(21);
        entity.WithoutInMemoryWrites(); builder.Build().Entities.Single().InMemoryStore!.ModelDefaultsEnabled.Should().BeFalse();
    }
    private static ModelDefaultGuardContext Context(string mode) => new(new DbContextOptionsBuilder<ModelDefaultContext>().UseSqlite("Data Source=:memory:")
        .ReplaceService<IModelCacheKeyFactory, ModelDefaultCacheKeyFactory>().Options, mode);

    private static RonSijm.RepoGen.Design.RepositoryModelBuilder GuardBuilder(string mode, bool strict)
    {
        var builder = Builder(strict: strict); if (mode != "computed") return builder;
        var entity = builder.Entity<ModelDefaultRow>();
        foreach (var name in new[] { "Create", "StageCreate", "CreateMany", "Reset" }) entity.ExcludeCommand(name);
        entity.HasCommand("Create").Create<ModelDefaultData>(request => new ModelDefaultRow { Id = request.Id, Enabled = request.Enabled,
            Level = request.Level, Label = request.Label, Score = request.Score, Priority = request.Priority })
            .AllowWrite(row => row.Id).AllowWrite(row => row.Enabled).AllowWrite(row => row.Level).AllowWrite(row => row.Label)
            .AllowWrite(row => row.Score).AllowWrite(row => row.Priority).Save();
        return builder;
    }
}
