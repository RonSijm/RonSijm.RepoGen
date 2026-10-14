using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class RepositoryBatchInputTests
{
    [Fact]
    public void OrderedInputAndKeysAreCapturedOnceAndCannotBeEdited()
    {
        var enumerations = 0;
        IEnumerable<InMemoryBatchItem> Items() { enumerations++; yield return new(2, "two"); yield return new(1, "one"); }
        var input = RepositoryBatchInputs.Prepare(Items(), item => item.Id, 3);
        input.Keys.Should().Equal(2, 1);
        input.Items.Select(pair => pair.Value.Name).Should().Equal("two", "one");
        enumerations.Should().Be(1);
        Action edit = () => ((IList<int>)input.Keys).Add(3);
        edit.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData("null-input")]
    [InlineData("null-item")]
    [InlineData("duplicate")]
    [InlineData("limit")]
    [InlineData("zero-limit")]
    [InlineData("unbounded")]
    public void InvalidInputsFailBeforeARepositoryCanRun(string kind)
    {
        InMemoryBatchItem[] items = kind switch
        {
            "null-input" => null!, "null-item" => [null!], "duplicate" => [new(1, "a"), new(1, "b")],
            _ => [new(1, "a"), new(2, "b")]
        };
        var limit = kind == "zero-limit" ? 0 : kind == "unbounded" ? int.MaxValue : kind == "limit" ? 1 : 3;
        Action prepare = () => RepositoryBatchInputs.Prepare(items, item => item.Id, limit);
        prepare.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NullKeysAndCancellationDuringEnumerationAreRejected()
    {
        Action nullKey = () => RepositoryBatchInputs.Prepare<InMemoryBatchItem, string>([new(1, "a")], _ => null!, 3);
        nullKey.Should().Throw<ArgumentException>().WithMessage("*non-null and unique*");
        using var cancellation = new CancellationTokenSource();
        IEnumerable<InMemoryBatchItem> Items() { yield return new(1, "a"); cancellation.Cancel(); }
        Action prepare = () => RepositoryBatchInputs.Prepare(Items(), item => item.Id, 3, cancellation.Token);
        prepare.Should().Throw<OperationCanceledException>();
    }
}
