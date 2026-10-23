using FluentAssertions;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningShadowBufferTests
{
    [Fact]
    public void TheDetachedBufferCopiesPropertyNamesAndPreservesModelValuesAndEntityIdentity()
    {
        var entity = new object(); var source = new Dictionary<string, object?> { ["Count"] = 7, ["Status"] = ReturningState.Ready, ["Note"] = null };
        var row = new ReturningWriteRow<object>(entity, source); source["Count"] = 99; source.Clear();
        row.Entity.Should().BeSameAs(entity); row.GetValue<int>("Count").Should().Be(7);
        row.GetValue<int?>("Count").Should().Be(7); row.GetValue<ReturningState?>("Status").Should().Be(ReturningState.Ready);
        row.GetValue<string?>("Note").Should().BeNull(); row.GetValue<int?>("Note").Should().BeNull();
    }

    [Theory]
    [InlineData("count")]
    [InlineData("missing")]
    public void NamesAreExactAndNeverExpressions(string name)
    {
        var row = new ReturningWriteRow<object>(new(), new Dictionary<string, object?> { ["Count"] = 1 });
        Action action = () => row.GetValue<int>(name);
        action.Should().Throw<InvalidOperationException>().WithMessage("*no mapped property*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullsAndValuesAreNotSilentlyCoercedToAnotherClrType(bool nullValue)
    {
        var row = new ReturningWriteRow<object>(new(), new Dictionary<string, object?> { ["Value"] = nullValue ? null : 2 });
        Action action = () => row.GetValue<long>("Value");
        action.Should().Throw<InvalidOperationException>().WithMessage("*requested CLR type*");
    }
}
