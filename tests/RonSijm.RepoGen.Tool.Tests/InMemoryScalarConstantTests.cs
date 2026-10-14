using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryScalarConstantTests
{
    public static IEnumerable<object?[]> Values()
    {
        foreach (var value in new object[] { (byte)255, (sbyte)-127, (short)-2, (ushort)3, -1, 4U, -5L, 6UL, 1.25F, -2.5D, 3.75M,
            true, "quote\"\\\0\n\u2028", '\0', ModelDefaultLevel.Unset, (ModelDefaultLevel)(-1), Guid.Parse("12345678-1234-1234-1234-123456789012"),
            new DateTime(123456789, DateTimeKind.Utc), new DateTimeOffset(638000000000000000, TimeSpan.FromHours(2)), TimeSpan.FromTicks(-99),
            new DateOnly(2020, 1, 2), new TimeOnly(12345678), float.PositiveInfinity, double.NegativeInfinity, double.NaN })
            yield return [value, value.GetType()];
        yield return [-1, typeof(int?)]; yield return [null, typeof(int?)]; yield return [null, typeof(string)];
    }
    [Theory]
    [MemberData(nameof(Values))]
    public void TypedConstantsCompileRoundTripAndCompareWithoutBoxingMismatch(object? value, Type type)
    {
        var source = InMemoryScalarConstantRenderer.RenderTyped(value, type); source.Should().NotBeNull();
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Append(typeof(ModelDefaultLevel).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("Constant_" + Guid.NewGuid().ToString("N"), [CSharpSyntaxTree.ParseText(
            "#nullable enable\npublic static class Constant { public static object? Get() => " + source + "; }")], refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream(); var result = compilation.Emit(stream);
        result.Diagnostics.Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Should().BeEmpty();
        stream.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        var actual = assembly.GetType("Constant")!.GetMethod("Get")!.Invoke(null, null);
        RonSijm.RepoGen.Testing.InMemoryValueEquality.AreEqual(actual, value).Should().BeTrue();
    }
    [Fact]
    public void UnsupportedValuesNeverInvokeArbitraryFormattingOrConvertTypes()
    {
        InMemoryScalarConstantRenderer.Render(new byte[] { 1 }, typeof(byte[])).Should().BeNull();
        InMemoryScalarConstantRenderer.Render(new object(), typeof(object)).Should().BeNull();
        InMemoryScalarConstantRenderer.Render(1, typeof(long)).Should().BeNull();
        InMemoryScalarConstantRenderer.Render(null, typeof(int)).Should().BeNull();
    }
}
