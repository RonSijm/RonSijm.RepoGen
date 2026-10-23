using System.Linq.Expressions;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalJoinGuardTests
{
    [Theory]
    [InlineData("plain", true)]
    [InlineData("reverse", true)]
    [InlineData("leading", true)]
    [InlineData("referenceCast", true)]
    [InlineData("trailing", false)]
    [InlineData("or", false)]
    [InlineData("negated", false)]
    [InlineData("indexed", false)]
    public void RecognizesOnlyExplicitLeadingBuiltInReferenceGuards(string shape, bool supported)
    {
        Expression<Func<ManyReadOwner, int>> expression = shape switch
        {
            "plain" => row => row.Tags.Where(tag => tag != null).Count(),
            "reverse" => row => row.Tags.Where(tag => null != tag).Count(),
            "leading" => row => row.Tags.Where(tag => tag != null && tag.Id > 1 && !tag.Hidden).Count(),
            "referenceCast" => row => row.Tags.Where(tag => (object)tag != null).Count(),
            "trailing" => row => row.Tags.Where(tag => tag.Id > 1 && tag != null).Count(),
            "or" => row => row.Tags.Where(tag => tag != null || tag!.Id > 1).Count(),
            "negated" => row => row.Tags.Where(tag => !(tag == null)).Count(),
            _ => row => row.Tags.Where((tag, index) => tag != null && index > 1).Count()
        };
        using var context = OptionalJoinTestHarness.Context();
        var inspector = new InMemoryOptionalJoinUsageInspector(context.Model.FindEntityType(typeof(ManyReadOwner))!.FindSkipNavigation(nameof(ManyReadOwner.Tags))!);
        inspector.Visit(expression);
        inspector.Supported.Should().Be(supported);
    }

    [Fact]
    public void ACustomComparisonIsNotProofOfReferenceIdentityAndIsNeverInvokedDuringInspection()
    {
        var row = Expression.Parameter(typeof(ManyReadOwner), "row");
        var target = Expression.Parameter(typeof(ManyReadTag), "tag");
        var comparison = Expression.NotEqual(target, Expression.Constant(null, typeof(ManyReadTag)), false,
            typeof(OptionalJoinGuardTests).GetMethod(nameof(CustomComparison), BindingFlags.Static | BindingFlags.NonPublic)!);
        var where = Expression.Call(typeof(Enumerable), nameof(Enumerable.Where), [typeof(ManyReadTag)],
            Expression.Property(row, nameof(ManyReadOwner.Tags)), Expression.Lambda<Func<ManyReadTag, bool>>(comparison, target));
        using var context = OptionalJoinTestHarness.Context();
        var inspector = new InMemoryOptionalJoinUsageInspector(context.Model.FindEntityType(typeof(ManyReadOwner))!.FindSkipNavigation(nameof(ManyReadOwner.Tags))!);
        inspector.Visit(where);
        inspector.Supported.Should().BeFalse();
    }

    private static bool CustomComparison(ManyReadTag? left, ManyReadTag? right)
        => throw new InvalidOperationException("Designer comparisons must not execute during inspection.");
}
