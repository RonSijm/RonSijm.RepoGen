using System.Linq.Expressions;
using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NestedProjectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NestedProjectionGuardTests
{
    [Theory]
    [InlineData("default", "RGD4601")]
    [InlineData("scalar", "RGD4601")]
    [InlineData("entity", "RGD4601")]
    [InlineData("entityList", "RGD4601")]
    [InlineData("entityMember", "RGD4601")]
    [InlineData("delegateMember", "RGD4601")]
    [InlineData("object", "RGD4601")]
    [InlineData("framework", "RGD4601")]
    [InlineData("dictionary", "RGD4601")]
    [InlineData("hashSet", "RGD4601")]
    [InlineData("multiArray", "RGD4601")]
    [InlineData("jagged", "RGD4601")]
    [InlineData("recursive", "RGD4601")]
    [InlineData("growing", "RGD4601")]
    [InlineData("collectionRoot", "RGD4601")]
    [InlineData("deferred", "RGD4602")]
    [InlineData("arrayBounds", "RGD4602")]
    [InlineData("captured", "RGD4602")]
    [InlineData("dropped", "RGD4602")]
    [InlineData("entityConstruction", "RGD4602")]
    [InlineData("clock", "RGD4602")]
    [InlineData("unknownHelper", "RGD4602")]
    [InlineData("cycle", "RGD4003")]
    public void NestedConsentDoesNotPermitEntityGraphsOrOpaqueOrDeferredData(string scenario, string diagnostic)
    {
        using var context = MemoryWriteTestHarness.Context(); var builder = Builder(translations: false, strict: false);
        var captured = new NestedProjectionDetail("captured", 1);
        switch (scenario)
        {
            case "default": builder.HasInMemoryProjectionTranslation<int, NestedProjectionCard>("Bad", id => ProjectionNoteCalls.Echo<NestedProjectionCard>(id),
                id => new NestedProjectionCard(id, null, Array.Empty<NestedProjectionDetail>(), Array.Empty<string>())); break;
            case "scalar": builder.HasInMemoryProjectionTranslation<int, int>("Bad", id => TranslationCalls.Echo(id), id => id, allowNestedData: true); break;
            case "entity": Register<MemoryWriteNote>(id => new MemoryWriteNote { Id = id }); break;
            case "entityList": Register<NestedProjectionBox<IReadOnlyList<MemoryWriteNote>>>(id => new(id, Array.Empty<MemoryWriteNote>())); break;
            case "entityMember": Register<NestedProjectionEntityContainer>(id => new(id, new MemoryWriteNote { Id = id })); break;
            case "delegateMember": Register<NestedProjectionBox<Func<int, int>>>(id => new(id, value => value + id)); break;
            case "object": Register<NestedProjectionBox<object>>(id => new(id, id)); break;
            case "framework": Register<NestedProjectionBox<Uri>>(id => new(id, new Uri("https://example.org"))); break;
            case "dictionary": Register<NestedProjectionBox<Dictionary<int, string>>>(id => new(id, new Dictionary<int, string>())); break;
            case "hashSet": Register<NestedProjectionBox<HashSet<int>>>(id => new(id, new HashSet<int>())); break;
            case "multiArray": Register<NestedProjectionBox<int[,]>>(id => new(id, new int[1, 1])); break;
            case "jagged": Register<NestedProjectionBox<int[][]>>(id => new(id, new[] { new[] { id } })); break;
            case "recursive": Register<NestedProjectionRecursive>(id => new(id, null)); break;
            case "growing": Register<NestedProjectionGrowing<int>>(id => new() { Id = id }); break;
            case "collectionRoot": Register<IReadOnlyList<NestedProjectionDetail>>(id => new[] { new NestedProjectionDetail("x", id) }); break;
            case "deferred": Register<NestedProjectionBox<IEnumerable<NestedProjectionDetail>>>(id => new(id,
                Enumerable.Range(0, id).Select(value => new NestedProjectionDetail("x", value)))); break;
            case "arrayBounds": Register<NestedProjectionCard>(id => new(id, null, Array.Empty<NestedProjectionDetail>(), new string[id])); break;
            case "captured": Register<NestedProjectionCard>(id => new(id, captured, Array.Empty<NestedProjectionDetail>(), Array.Empty<string>())); break;
            case "dropped": Register<NestedProjectionCard>(id => new(1, null, Array.Empty<NestedProjectionDetail>(), Array.Empty<string>())); break;
            case "entityConstruction": Register<NestedProjectionCard>(id => new(id, new NestedProjectionDetail(new MemoryWriteNote { Title = "bad" }.Title, id),
                Array.Empty<NestedProjectionDetail>(), Array.Empty<string>())); break;
            case "clock": Register<NestedProjectionCard>(id => new(id, new NestedProjectionDetail("x", DateTime.UtcNow.Second), Array.Empty<NestedProjectionDetail>(), Array.Empty<string>())); break;
            case "unknownHelper": Register<NestedProjectionCard>(id => new(id, new NestedProjectionDetail("x", TranslationCalls.Measure("x")), Array.Empty<NestedProjectionDetail>(), Array.Empty<string>())); break;
            case "cycle": Register<NestedProjectionCard>(id => ProjectionNoteCalls.Echo<NestedProjectionCard>(id)); break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(diagnostic + ":*");

        void Register<T>(Expression<Func<int, T>> expression) => builder.HasInMemoryProjectionTranslation<int, T>("Bad",
            id => ProjectionNoteCalls.Echo<T>(id), expression, allowNestedData: true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASharedRegistryDoesNotGiveFlatOrScalarRecipesNestedPermission(bool projection)
    {
        using var context = MemoryWriteTestHarness.Context(); var builder = Builder(translations: false, strict: false);
        builder.HasInMemoryProjectionTranslation<int, NestedProjectionCard>("Nested", id => ProjectionNoteCalls.Echo<NestedProjectionCard>(id),
            id => new NestedProjectionCard(id, null, Array.Empty<NestedProjectionDetail>(), Array.Empty<string>()), allowNestedData: true);
        if (projection) builder.HasInMemoryProjectionTranslation<int, ProjectionNoteStamp>("Flat", id => ProjectionNoteCalls.Stamp(id),
            id => new ProjectionNoteStamp(ProjectionNoteCalls.Echo<NestedProjectionCard>(id).Id));
        else builder.HasInMemoryTranslation<int, int>("Scalar", id => TranslationCalls.Echo(id), id => ProjectionNoteCalls.Echo<NestedProjectionCard>(id).Id);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage((projection ? "RGD4602" : "RGD4002") + ":*");
    }

    [Fact]
    public void NestedDeclarationsRequireFinalModelValidation()
    {
        var builder = new RepositoryModelBuilder().HasInMemoryProjectionTranslation<int, NestedProjectionCard>("Nested",
            id => ProjectionNoteCalls.Echo<NestedProjectionCard>(id),
            id => new NestedProjectionCard(id, null, Array.Empty<NestedProjectionDetail>(), Array.Empty<string>()), allowNestedData: true);
        Action inspect = () => { _ = new InMemoryTranslationExpressions(builder.Build().InMemoryTranslations); };
        inspect.Should().Throw<InvalidOperationException>().WithMessage("RGD4601:*");
    }

    [Fact]
    public void NestedPermissionCannotCallPrivateConstructors()
    {
        using var context = MemoryWriteTestHarness.Context(); var builder = Builder(translations: false, strict: false);
        var parameter = Expression.Parameter(typeof(int), "id");
        var constructor = typeof(ProjectionTranslationGuardTests.PrivateConstructorDto).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Single();
        builder.HasInMemoryProjectionTranslation<int, ProjectionTranslationGuardTests.PrivateConstructorDto>("Bad",
            id => ProjectionNoteCalls.Echo<ProjectionTranslationGuardTests.PrivateConstructorDto>(id),
            Expression.Lambda<Func<int, ProjectionTranslationGuardTests.PrivateConstructorDto>>(Expression.New(constructor, parameter), parameter), allowNestedData: true);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4602:*public constructors*");
    }

    [Theory]
    [InlineData(16, true)]
    [InlineData(17, false)]
    public void DataTypeInspectionHasABoundedDepthEvenForClosedGenericShapes(int levels, bool supported)
    {
        using var context = MemoryWriteTestHarness.Context();
        var type = typeof(int);
        for (var level = 0; level < levels; level++) type = typeof(NestedProjectionBox<>).MakeGenericType(type);
        (InMemoryProjectionDataShape.Create(type, context.Model) is not null).Should().Be(supported);
    }

    [Fact]
    public void NestedInitializersCannotWriteStaticData()
    {
        using var context = MemoryWriteTestHarness.Context(); var builder = Builder(translations: false, strict: false);
        var parameter = Expression.Parameter(typeof(int), "id");
        var type = typeof(ProjectionTranslationGuardTests.StaticDto);
        var expression = Expression.MemberInit(Expression.New(type),
            Expression.Bind(type.GetProperty("Id")!, parameter), Expression.Bind(type.GetProperty("Counter")!, parameter));
        builder.HasInMemoryProjectionTranslation<int, ProjectionTranslationGuardTests.StaticDto>("Bad",
            id => ProjectionNoteCalls.Echo<ProjectionTranslationGuardTests.StaticDto>(id),
            Expression.Lambda<Func<int, ProjectionTranslationGuardTests.StaticDto>>(expression, parameter), allowNestedData: true);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4602:*public writable data members*");
    }

    [Fact]
    public void InitializersCannotImplicitlyMutateAConstructorsCollection()
    {
        using var context = MemoryWriteTestHarness.Context(); var builder = Builder(translations: false, strict: false);
        builder.HasInMemoryProjectionTranslation<int, NestedProjectionEditable>("Bad", id => ProjectionNoteCalls.Echo<NestedProjectionEditable>(id),
            id => new NestedProjectionEditable { Id = id, Items = { new NestedProjectionDetail("x", id) } }, allowNestedData: true);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4602:*implicit collection member mutation*");
    }
}
