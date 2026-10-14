using FluentAssertions;
using System.Linq.Expressions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProjectionTranslationGuardTests
{
    [Theory]
    [InlineData("scalar", "RGD4601")]
    [InlineData("entity", "RGD4601")]
    [InlineData("graph", "RGD4601")]
    [InlineData("object", "RGD4601")]
    [InlineData("private", "RGD4601")]
    [InlineData("scalarApi", "RGD4001")]
    [InlineData("notCall", "RGD4001")]
    [InlineData("computedInput", "RGD4001")]
    [InlineData("captured", "RGD4602")]
    [InlineData("dropped", "RGD4602")]
    [InlineData("entityConstruction", "RGD4602")]
    [InlineData("clock", "RGD4602")]
    [InlineData("unknownHelper", "RGD4602")]
    [InlineData("cycle", "RGD4003")]
    public void DtoRegistrationCannotHideNonportableArgumentsStateOrGraphConstruction(string scenario, string diagnostic)
    {
        using var context = MemoryWriteTestHarness.Context(); var builder = ProjectionTranslationTestHarness.Builder(translations: false, strict: false);
        var captured = "secret";
        switch (scenario)
        {
            case "scalar": builder.HasInMemoryProjectionTranslation<int, int>("Bad", value => TranslationCalls.Echo(value), value => value); break;
            case "entity": builder.HasInMemoryProjectionTranslation<int, MemoryWriteNote>("Bad", id => ProjectionNoteCalls.Echo<MemoryWriteNote>(id), id => new MemoryWriteNote { Id = id }); break;
            case "graph": builder.HasInMemoryProjectionTranslation<int, ProjectionNoteGraph>("Bad", id => ProjectionNoteCalls.Echo<ProjectionNoteGraph>(id), id => new ProjectionNoteGraph(id, Array.Empty<ProjectionNoteCard>())); break;
            case "object": builder.HasInMemoryProjectionTranslation<int, object>("Bad", id => ProjectionNoteCalls.Echo<object>(id), id => new ProjectionNoteData { Id = id }); break;
            case "private": builder.HasInMemoryProjectionTranslation<int, HiddenDto>("Bad", id => ProjectionNoteCalls.Echo<HiddenDto>(id), id => new HiddenDto(id)); break;
            case "scalarApi": builder.HasInMemoryTranslation<int, ProjectionNoteStamp>("Bad", id => ProjectionNoteCalls.Stamp(id), id => new ProjectionNoteStamp(id)); break;
            case "notCall": builder.HasInMemoryProjectionTranslation<int, ProjectionNoteStamp>("Bad", id => new ProjectionNoteStamp(id), id => new ProjectionNoteStamp(id)); break;
            case "computedInput": builder.HasInMemoryProjectionTranslation<int, ProjectionNoteStamp>("Bad", id => ProjectionNoteCalls.Stamp(id + 1), id => new ProjectionNoteStamp(id)); break;
            case "captured": builder.HasInMemoryProjectionTranslation<int, string, ProjectionNoteData>("Bad", (id, title) => ProjectionNoteCalls.Data(id, title), (id, title) => new ProjectionNoteData { Id = id, Caption = captured + title }); break;
            case "dropped": builder.HasInMemoryProjectionTranslation<int, string, ProjectionNoteData>("Bad", (id, title) => ProjectionNoteCalls.Data(id, title), (id, title) => new ProjectionNoteData { Id = id }); break;
            case "entityConstruction": builder.HasInMemoryProjectionTranslation<int, string, ProjectionNoteData>("Bad", (id, title) => ProjectionNoteCalls.Data(id, title), (id, title) => new ProjectionNoteData { Id = id, Caption = new MemoryWriteNote { Title = title }.Title }); break;
            case "clock": builder.HasInMemoryProjectionTranslation<int, string, ProjectionNoteData>("Bad", (id, title) => ProjectionNoteCalls.Data(id, title), (id, title) => new ProjectionNoteData { Id = id, Caption = title + DateTime.UtcNow.Second }); break;
            case "unknownHelper": builder.HasInMemoryProjectionTranslation<int, string, ProjectionNoteData>("Bad", (id, title) => ProjectionNoteCalls.Data(id, title), (id, title) => new ProjectionNoteData { Id = id, Caption = title + TranslationCalls.Measure(title) }); break;
            case "cycle": builder.HasInMemoryProjectionTranslation<int, string, ProjectionNoteData>("Bad", (id, title) => ProjectionNoteCalls.Data(id, title), (id, title) => ProjectionNoteCalls.Data(id, title)); break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage(diagnostic + ":*");
    }

    [Fact]
    public void ScalarAndDtoRegistrationsShareExactOverloadAndNameUniqueness()
    {
        var builder = new RepositoryModelBuilder().HasInMemoryProjectionTranslation<int, ProjectionNoteStamp>("Stamp", id => ProjectionNoteCalls.Stamp(id), id => new ProjectionNoteStamp(id));
        Action name = () => builder.HasInMemoryTranslation<int, int>("Stamp", id => TranslationCalls.Echo(id), id => id);
        Action method = () => builder.HasInMemoryTranslation<int, ProjectionNoteStamp>("Other", id => ProjectionNoteCalls.Stamp(id), id => new ProjectionNoteStamp(id));
        name.Should().Throw<InvalidOperationException>().WithMessage("RGD4001:*"); method.Should().Throw<InvalidOperationException>().WithMessage("RGD4001:*");
    }

    private sealed record HiddenDto(int Id);

    [Fact]
    public void APrivateDtoConstructorCannotBeEmittedIntoSeparateTestingOutput()
    {
        using var context = MemoryWriteTestHarness.Context(); var builder = ProjectionTranslationTestHarness.Builder(translations: false, strict: false);
        var parameter = Expression.Parameter(typeof(int), "id");
        var constructor = typeof(PrivateConstructorDto).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Single();
        var replacement = Expression.Lambda<Func<int, PrivateConstructorDto>>(Expression.New(constructor, parameter), parameter);
        builder.HasInMemoryProjectionTranslation<int, PrivateConstructorDto>("PrivateCtor", id => ProjectionNoteCalls.Echo<PrivateConstructorDto>(id), replacement);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4602:*public scalar constructors*");
    }

    [Fact]
    public void ProjectionDeclarationsRequireModelAwareValidationButExistingScalarDeclarationsDoNot()
    {
        var builder = new RepositoryModelBuilder().HasInMemoryProjectionTranslation<int, ProjectionNoteStamp>("Stamp", id => ProjectionNoteCalls.Stamp(id), id => new ProjectionNoteStamp(id));
        Action inspect = () => { _ = new InMemoryTranslationExpressions(builder.Build().InMemoryTranslations); };
        inspect.Should().Throw<InvalidOperationException>().WithMessage("RGD4601:*");
    }

    public sealed class PrivateConstructorDto
    {
        private PrivateConstructorDto(int id) => Id = id;
        public int Id { get; }
    }

    [Fact]
    public void InitializersCannotWriteStaticDtoState()
    {
        using var context = MemoryWriteTestHarness.Context(); var builder = ProjectionTranslationTestHarness.Builder(translations: false, strict: false);
        var parameter = Expression.Parameter(typeof(int), "id");
        var body = Expression.MemberInit(Expression.New(typeof(StaticDto)),
            Expression.Bind(typeof(StaticDto).GetProperty(nameof(StaticDto.Id))!, parameter),
            Expression.Bind(typeof(StaticDto).GetProperty(nameof(StaticDto.Counter))!, parameter));
        builder.HasInMemoryProjectionTranslation<int, StaticDto>("Static", id => ProjectionNoteCalls.Echo<StaticDto>(id),
            Expression.Lambda<Func<int, StaticDto>>(body, parameter));
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4602:*writable scalar members*");
    }

    public sealed class StaticDto
    {
        public int Id { get; set; }
        public static int Counter { get; set; }
    }
}
