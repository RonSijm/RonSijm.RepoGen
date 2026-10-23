using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ArrayTypeFidelityTests
{
    [Fact]
    public void MixedArrayRanksAndAnnotationsRoundTripThroughTheCSharpCompiler()
    {
        var properties = typeof(ArrayTypeShapes).GetProperties();
        var info = new NullabilityInfoContext();
        var annotated = properties.Select(property => "public " + RepositoryTypeName.Annotated(info.Create(property)) + " " + property.Name + " { get; set; }");
        var clr = properties.Select(property => "public " + RepositoryTypeName.Clr(property.PropertyType) + " " + property.Name + " { get; set; }");
        var source = "#nullable enable\npublic class ArrayAnnotationCopy { " + string.Join("\n", annotated) + " }\n#nullable disable\npublic class ArrayClrCopy { " + string.Join("\n", clr) + " }";
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("Arrays" + Guid.NewGuid().ToString("N"), [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        emitted.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var assembly = Assembly.Load(output.ToArray());
        foreach (var property in properties)
        {
            var copy = assembly.GetType("ArrayAnnotationCopy")!.GetProperty(property.Name)!;
            copy.PropertyType.Should().Be(property.PropertyType);
            assembly.GetType("ArrayClrCopy")!.GetProperty(property.Name)!.PropertyType.Should().Be(property.PropertyType);
            Tree(info.Create(copy)).Should().Be(Tree(info.Create(property)));
        }
    }

    [Fact]
    public void ExplicitMixedArrayAnnotationsBindTheRealPortAndCompileWithoutWarnings()
    {
        using var context = new ClosedContext(new DbContextOptionsBuilder<ClosedContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Arrays.Contracts").GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<ClosedRow>();
        entity.WriteEnabled = false;
        entity.BindReadPort<IArrayShapePort>();
        entity.HasQuery("Count").InputNullability<string[,][]>("values", type => type.Nullable().Element(item => item.Element(value => value.Nullable())))
            .Where<string?[,][]?>((row, values) => values == null || values.Length > row.Id).Count();
        var model = RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: builder.Build());
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(model);
        contracts.Should().NotContain("String[][,]");
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        typeof(IArrayShapePort).IsAssignableFrom(assembly.GetType(typeof(ClosedRow).Namespace + ".ClosedRowRepositoryOperations")).Should().BeTrue();
    }

    private static string Tree(System.Reflection.NullabilityInfo info) => info.Type + ":" + info.ReadState + ":" + info.WriteState +
                                                                          (info.ElementType is null ? "" : "[" + Tree(info.ElementType) + "]") + "<" + string.Join(",", info.GenericTypeArguments.Select(Tree)) + ">";
}

#pragma warning disable CA1819 // Fixture deliberately exposes arrays to test emitted contract types.
public sealed class ArrayTypeShapes
{
    public string?[,][]? RectangularThenJagged { get; set; }
    public string?[][,]? JaggedThenRectangular { get; set; }
    public string?[]?[,]? NullableInner { get; set; }
    public string?[,][]?[,] Deep { get; set; } = default!;
    public List<string?[,][]>?[][,]? GenericElements { get; set; }
    public int?[][,,][,] Values { get; set; } = new int?[1][,,][,];
}
#pragma warning restore CA1819
public interface IArrayShapePort
{
    Task<int> CountAsync(string?[,][]? values, CancellationToken cancellationToken = default);
}