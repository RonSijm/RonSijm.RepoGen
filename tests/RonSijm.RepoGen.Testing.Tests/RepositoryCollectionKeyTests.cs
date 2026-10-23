using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class RepositoryCollectionKeyTests
{
    public static IEnumerable<object[]> NullComponents() => Enumerable.Range(2, 6)
        .SelectMany(arity => Enumerable.Range(0, arity).Select(index => new object[] { arity, index }));

    [Theory]
    [MemberData(nameof(NullComponents))]
    public void EveryComponentAtEverySupportedArityIsValidated(int arity, int index)
    {
        var values = Enumerable.Repeat("sensitive", 7).ToArray(); values[index] = null!;
        Action require = () => Require(arity, values);
        require.Should().Throw<ArgumentException>().WithMessage("Collection matching key components must be non-null.");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void ValidKeysAreReturnedUnmodified(int arity)
    {
        var values = Enumerable.Range(1, 7).Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Require(arity, values).ToString().Should().Be("(" + string.Join(", ", values.Take(arity)) + ")");
    }

    [Fact]
    public void InputIsEvaluatedOnceAndDefaultTupleEqualityDoesNotBecomeProviderOrFixtureEquality()
    {
        var calls = 0;
        (string, int) Input() { calls++; return ("Code", 3); }
        RepositoryCollectionKeys.Require(Input()).Should().Be(("Code", 3)); calls.Should().Be(1);
        RepositoryCollectionKeys.Require(("Code", "en")).Should().NotBe(RepositoryCollectionKeys.Require(("code", "en")));
        var input = RepositoryBatchInputs.Prepare(new[] { ("title", "en"), ("title", "nl") }, row => RepositoryCollectionKeys.Require(row), 5);
        input.Items.Should().HaveCount(2);
        Action duplicate = () => RepositoryBatchInputs.Prepare(new[] { ("title", "en"), ("title", "en") }, row => RepositoryCollectionKeys.Require(row), 5);
        duplicate.Should().Throw<ArgumentException>().WithMessage("*non-null and unique*");
    }

    [Fact]
    public void NullableValueComponentsAreRejectedEvenThoughTheTupleIsNotNull()
    {
        Action require = () => RepositoryCollectionKeys.Require((1, (int?)null));
        require.Should().Throw<ArgumentException>();
        RepositoryCollectionKeys.Require((1, (int?)2)).Should().Be((1, 2));
    }

    private static object Require(int arity, string[] values) => arity switch
    {
        2 => RepositoryCollectionKeys.Require((values[0], values[1])),
        3 => RepositoryCollectionKeys.Require((values[0], values[1], values[2])),
        4 => RepositoryCollectionKeys.Require((values[0], values[1], values[2], values[3])),
        5 => RepositoryCollectionKeys.Require((values[0], values[1], values[2], values[3], values[4])),
        6 => RepositoryCollectionKeys.Require((values[0], values[1], values[2], values[3], values[4], values[5])),
        7 => RepositoryCollectionKeys.Require((values[0], values[1], values[2], values[3], values[4], values[5], values[6])),
        _ => throw new ArgumentOutOfRangeException(nameof(arity))
    };
}
