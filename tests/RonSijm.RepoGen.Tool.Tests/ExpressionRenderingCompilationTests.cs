using System.Linq.Expressions;
using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ExpressionRenderingCompilationTests
{
    [Theory]
    [InlineData(typeof(int), false, false)]
    [InlineData(typeof(int), false, true)]
    [InlineData(typeof(int), true, false)]
    [InlineData(typeof(int), true, true)]
    [InlineData(typeof(string), false, false)]
    [InlineData(typeof(string), false, true)]
    [InlineData(typeof(string), true, false)]
    [InlineData(typeof(string), true, true)]
    public void ArrayMembershipThroughReadOnlySpanCompilesAsEnumerableContains(Type element, bool unaryConversion, bool comparer)
    {
        var values = Expression.Parameter(element.MakeArrayType(), "values");
        var value = Expression.Parameter(element, "value");
        var span = typeof(ReadOnlySpan<>).MakeGenericType(element);
        var conversion = span.GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static, [values.Type])!;
        Expression converted = unaryConversion ? Expression.Convert(values, span, conversion) : Expression.Call(conversion, values);
        var method = typeof(MemoryExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static).Single(candidate =>
            candidate.Name == "Contains" && candidate.IsGenericMethodDefinition && candidate.GetParameters().Length == (comparer ? 3 : 2) &&
            candidate.GetParameters()[0].ParameterType.IsGenericType && candidate.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>))
            .MakeGenericMethod(element);
        var equality = Expression.Parameter(typeof(IEqualityComparer<>).MakeGenericType(element), "comparer");
        var arguments = comparer ? new[] { converted, value, equality } : new[] { converted, value };
        var parameters = comparer ? new[] { values, value, equality } : new[] { values, value };
        var expression = Expression.Lambda(Expression.Call(method, arguments), parameters);

        var body = RenderAndCompile(expression);
        body.Should().Contain("global::System.Linq.Enumerable.Contains<").And.NotContain("op_Implicit").And.NotContain("MemoryExtensions");
        if (comparer) body.Should().Contain(", comparer)");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConvertingNegatedValuesAndNegativeConstantsCompiles(bool checkedConversion, bool constant)
    {
        var value = Expression.Parameter(typeof(int), "value");
        Expression operand = constant ? Expression.Constant(-2) : Expression.Negate(value);
        var converted = checkedConversion ? Expression.ConvertChecked(operand, typeof(double)) : Expression.Convert(operand, typeof(double));
        RenderAndCompile(Expression.Lambda(converted, value)).Should().Contain("global::System.Double");
    }

    [Fact]
    public void ExplicitNullableValueAccessCompilesWithoutNullableWarnings()
    {
        Expression<Func<int?, int>> expression = value => value!.Value;
        RenderAndCompile(expression).Should().Contain("value!.Value");
    }

    private static string RenderAndCompile(LambdaExpression expression)
    {
        var body = ProjectionExpressionRenderer.RenderBody(expression, expression.Parameters.Select(parameter => parameter.Name!).ToArray());
        var parameters = string.Join(", ", expression.Parameters.Select(parameter => RepositoryModelFactory.CSharpTypeName(parameter.Type) + " " + parameter.Name));
        var source = "#nullable enable\npublic static class RenderedExpression { public static " + RepositoryModelFactory.CSharpTypeName(expression.ReturnType) +
            " Evaluate(" + parameters + ") => " + body + "; }";
        var compilation = CSharpCompilation.Create("RenderedExpression", [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12))],
            ClosedRepositoryTests.PlatformReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var output = new MemoryStream();
        compilation.Emit(output).Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString()).Should().BeEmpty("the rendered expression must compile under C# 12: {0}", source);
        return body;
    }
}
