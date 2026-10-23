using System.Linq.Expressions;
using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredReferenceSafetyTests
{
    public static TheoryData<Expression<Func<ModelFilterRow, int>>, bool> Expressions => new()
    {
        { row => row.Folder!.Id, false },
        { row => row.Folder == null ? -1 : row.Folder.Id, true },
        { row => row.Folder != null ? row.Folder.Id : -1, true },
        { row => row.Folder != null && row.Folder.Id > 0 ? row.Folder.Id : -1, true },
        { row => row.Folder == null || row.Folder.Id < 0 ? -1 : row.Folder.Id, true },
        { row => (row.Folder == null ? -1 : row.Folder.Id) + row.Folder!.Id, false },
        { row => row.FolderId != null ? row.Folder!.Id : -1, false }
    };

    [Theory]
    [MemberData(nameof(Expressions))]
    public void OnlyLocalGuardsProveFilteredReferenceSafety(Expression<Func<ModelFilterRow, int>> expression, bool supported)
    {
        using var context = ModelFilterTestHarness.Context();
        var navigation = context.Model.FindEntityType(typeof(ModelFilterRow))!.FindNavigation(nameof(ModelFilterRow.Folder))!;
        new InMemoryFilteredReferenceUsageInspector(navigation).Check(expression).Should().Be(supported);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnguardedFilteredReferenceAndCrossStageGuardsRequireOverrides(bool mapped)
    {
        using var context = ModelFilterTestHarness.Context(); var builder = FilteredNavigationTestHarness.Base(mapped);
        var row = builder.Entity<ModelFilterRow>();
        row.HasQuery("Unsafe").Where(item => item.Folder != null).Select(item => item.Folder!.Id).List();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*filtered optional target*local explicit null guards*");
    }

    [Fact]
    public void GuardForOneRowDoesNotProveAnotherRowAndRelatedIdentityCannotEscape()
    {
        using var context = ModelFilterTestHarness.Context();
        var navigation = context.Model.FindEntityType(typeof(ModelFilterRow))!.FindNavigation(nameof(ModelFilterRow.Folder))!;
        Expression<Func<ModelFilterRow, ModelFilterRow, int>> wrong = (left, right) => left.Folder != null ? right.Folder!.Id : -1;
        Expression<Func<ModelFilterRow, object?>> escaped = row => row.Folder;
        new InMemoryFilteredReferenceUsageInspector(navigation).Check(wrong).Should().BeFalse();
        new InMemoryFilteredReferenceUsageInspector(navigation).Check(escaped).Should().BeFalse();
    }
}
