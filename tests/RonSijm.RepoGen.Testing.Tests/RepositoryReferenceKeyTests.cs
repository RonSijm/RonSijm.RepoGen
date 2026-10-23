using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class RepositoryReferenceKeyTests
{
    private static readonly int[] OneKey = [1];
    [Fact]
    public void ValidationEnumeratesOnceAndDoesNotRewriteKeys()
    {
        var owner = new object(); var enumerations = 0; var calls = 0;
        IEnumerable<(int, string)> Keys()
        {
            enumerations++; yield return (1, "a"); yield return (1, "b");
        }
        RepositoryReferenceKeys.RequireOwner(owner, Keys(), (actual, key) => { calls++; actual.Should().BeSameAs(owner); return key.Item1 == 1; });
        enumerations.Should().Be(1); calls.Should().Be(2);
    }

    [Fact]
    public void AReferenceMismatchIsAnExplicitErrorNotAKeyNormalization()
    {
        var owner = new object(); var keys = new[] { "Scope", "scope" };
        Action validate = () => RepositoryReferenceKeys.RequireOwner(owner, keys, (_, key) => key == "Scope");
        validate.Should().Throw<ArgumentException>().WithMessage("*selected owner*"); keys.Should().Equal("Scope", "scope");
    }

    [Fact]
    public void PreCancelledValidationNeverCallsThePredicate()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var calls = 0;
        Action validate = () => RepositoryReferenceKeys.RequireOwner(new object(), OneKey, (_, _) => { calls++; return true; }, cancellation.Token);
        validate.Should().Throw<OperationCanceledException>(); calls.Should().Be(0);
    }

    [Fact]
    public void ValidationChecksCancellationAfterTheFinalPredicate()
    {
        using var cancellation = new CancellationTokenSource();
        Action validate = () => RepositoryReferenceKeys.RequireOwner(new object(), OneKey, (_, _) => { cancellation.Cancel(); return true; }, cancellation.Token);
        validate.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("keys")]
    [InlineData("matchesOwner")]
    public void RequiredInputsAreChecked(string parameter)
    {
        Action validate = () => RepositoryReferenceKeys.RequireOwner<object, int>(parameter == "owner" ? null! : new object(),
            parameter == "keys" ? null! : OneKey, parameter == "matchesOwner" ? null! : static (_, _) => true);
        validate.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be(parameter);
    }
}
