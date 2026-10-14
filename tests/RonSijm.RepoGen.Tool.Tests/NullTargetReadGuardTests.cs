using System.Linq.Expressions;
using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NullTargetReadGuardTests
{
    [Theory]
    [InlineData("count", true)]
    [InlineData("methodCount", true)]
    [InlineData("missing", true)]
    [InlineData("any", true)]
    [InlineData("all", true)]
    [InlineData("conditional", true)]
    [InlineData("reverse", true)]
    [InlineData("negated", true)]
    [InlineData("or", true)]
    [InlineData("filtered", true)]
    [InlineData("sorted", true)]
    [InlineData("scalarDistinct", true)]
    [InlineData("scalarFirst", true)]
    [InlineData("unsafeSelect", false)]
    [InlineData("unsafeWhere", false)]
    [InlineData("unsafeSort", false)]
    [InlineData("tooLate", false)]
    [InlineData("wrongBranch", false)]
    [InlineData("unsafeOr", false)]
    [InlineData("indexed", false)]
    [InlineData("entity", false)]
    [InlineData("objectEscape", false)]
    [InlineData("navigationEscape", false)]
    [InlineData("entityDistinct", false)]
    [InlineData("raw", false)]
    [InlineData("first", false)]
    [InlineData("defaultIfEmpty", false)]
    public void OnlyBoundedNullSafeScalarAndDtoPipelinesAreAccepted(string shape, bool supported)
    {
        Expression<Func<ManyReadOwner, object>> expression = shape switch
        {
            "count" => row => row.Tags.Count,
            "methodCount" => row => row.Tags.Count(),
            "missing" => row => row.Tags.Count(tag => tag == null),
            "any" => row => row.Tags.Any(tag => tag != null && tag.Name == "Blue"),
            "all" => row => row.Tags.All(tag => tag == null || tag.Id > 0),
            "conditional" => row => row.Tags.Select(tag => tag == null ? null : tag.Name).ToList(),
            "reverse" => row => row.Tags.Select(tag => null == tag ? null : tag.Name).ToList(),
            "negated" => row => row.Tags.Where(tag => !(tag == null)).Select(tag => tag.Name).ToList(),
            "or" => row => row.Tags.Where(tag => tag == null || tag.Id > 0).Count(),
            "filtered" => row => row.Tags.Where(tag => tag != null).OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList(),
            "sorted" => row => row.Tags.OrderBy(tag => tag == null ? 0 : tag.Id).Select(tag => tag == null ? null : tag.Name).ToList(),
            "scalarDistinct" => row => row.Tags.Select(tag => tag == null ? null : tag.Name).Distinct().Count(),
            "scalarFirst" => row => row.Tags.Select(tag => tag == null ? null : tag.Name).FirstOrDefault()!,
            "unsafeSelect" => row => row.Tags.Select(tag => tag.Name).ToList(),
            "unsafeWhere" => row => row.Tags.Where(tag => tag.Id > 0).Count(),
            "unsafeSort" => row => row.Tags.OrderBy(tag => tag.Id).Count(),
            "tooLate" => row => row.Tags.OrderBy(tag => tag.Id).Where(tag => tag != null).Count(),
            "wrongBranch" => row => row.Tags.Select(tag => tag == null ? tag!.Name : "").ToList(),
            "unsafeOr" => row => row.Tags.Where(tag => tag != null || tag!.Id > 0).Count(),
            "indexed" => row => row.Tags.Where((tag, index) => tag != null && index > 0).Count(),
            "entity" => row => row.Tags.Select(tag => tag).ToList(),
            "objectEscape" => row => row.Tags.Select(tag => tag == null ? null : (object)tag).ToList(),
            "navigationEscape" => row => row.Tags.Where(tag => tag != null).Select(tag => tag.Owners).Count(),
            "entityDistinct" => row => row.Tags.Distinct().Count(),
            "first" => row => row.Tags.FirstOrDefault()!,
            "defaultIfEmpty" => row => row.Tags.DefaultIfEmpty().Count(),
            _ => row => row.Tags
        };
        using var context = OptionalJoinTestHarness.Context();
        var inspector = new InMemoryNullSafeJoinUsageInspector(context.Model.FindEntityType(typeof(ManyReadOwner))!.FindSkipNavigation(nameof(ManyReadOwner.Tags))!);
        inspector.Visit(expression); inspector.Supported.Should().Be(supported);
    }

    [Fact]
    public void EachDagOccurrenceNeedsItsOwnProofAndGuardStateCannotLeakBetweenBranches()
    {
        var owner = Expression.Parameter(typeof(ManyReadOwner), "row"); var tag = Expression.Parameter(typeof(ManyReadTag), "tag");
        var member = Expression.Property(owner, nameof(ManyReadOwner.Tags));
        var guarded = Expression.Condition(Expression.Equal(tag, Expression.Constant(null, typeof(ManyReadTag))), Expression.Constant(null, typeof(string)), Expression.Property(tag, nameof(ManyReadTag.Name)));
        var safe = Expression.Call(typeof(Enumerable), nameof(Enumerable.Select), [typeof(ManyReadTag), typeof(string)], member, Expression.Lambda<Func<ManyReadTag, string?>>(guarded, tag));
        var unsafeUse = Expression.Call(typeof(Enumerable), nameof(Enumerable.Select), [typeof(ManyReadTag), typeof(string)], member, Expression.Lambda<Func<ManyReadTag, string>>(Expression.Property(tag, nameof(ManyReadTag.Name)), tag));
        using var context = OptionalJoinTestHarness.Context();
        var inspector = new InMemoryNullSafeJoinUsageInspector(context.Model.FindEntityType(typeof(ManyReadOwner))!.FindSkipNavigation(nameof(ManyReadOwner.Tags))!);
        inspector.Visit(safe); inspector.Supported.Should().BeTrue(); inspector.Visit(unsafeUse); inspector.Supported.Should().BeFalse();
    }

    [Fact]
    public void OverloadedReferenceComparisonsAreNotNullSafetyProofAndNeverExecuteDuringInspection()
    {
        var row = Expression.Parameter(typeof(ManyReadOwner), "row"); var tag = Expression.Parameter(typeof(ManyReadTag), "tag");
        var comparison = Expression.NotEqual(tag, Expression.Constant(null, typeof(ManyReadTag)), false,
            typeof(NullTargetReadGuardTests).GetMethod(nameof(Compare), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!);
        var where = Expression.Call(typeof(Enumerable), nameof(Enumerable.Where), [typeof(ManyReadTag)], Expression.Property(row, nameof(ManyReadOwner.Tags)),
            Expression.Lambda<Func<ManyReadTag, bool>>(comparison, tag));
        using var context = OptionalJoinTestHarness.Context();
        var inspector = new InMemoryNullSafeJoinUsageInspector(context.Model.FindEntityType(typeof(ManyReadOwner))!.FindSkipNavigation(nameof(ManyReadOwner.Tags))!);
        inspector.Visit(where); inspector.Supported.Should().BeFalse();
    }

    private static bool Compare(ManyReadTag? left, ManyReadTag? right) => throw new InvalidOperationException("Designer comparisons must not execute.");
}
