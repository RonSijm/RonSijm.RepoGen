using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class RepositoryCreateInputsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void RequiresAFinitePositiveLimit(int limit)
    {
        Action call = () => RepositoryCreateInputs.Prepare(Array.Empty<string>(), limit);
        call.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EnumeratesOnceKeepsInputOrderAndDoesNotInventMatchingKeys()
    {
        var calls = 0;
        IEnumerable<string> Items() { calls++; yield return "same"; yield return "same"; }
        var result = RepositoryCreateInputs.Prepare(Items(), 2);
        result.Should().Equal("same", "same"); calls.Should().Be(1);
        result.Should().BeAssignableTo<IList<string>>().Subject.IsReadOnly.Should().BeTrue();
    }

    [Theory]
    [InlineData("limit")]
    [InlineData("null")]
    [InlineData("throw")]
    [InlineData("cancel")]
    public void FailureDisposesTheEnumeratorAndStopsBeforeFurtherItems(string mode)
    {
        var disposed = false; var visits = 0; using var cancellation = new CancellationTokenSource();
        IEnumerable<string> Items()
        {
            try
            {
                visits++; yield return "first";
                visits++;
                if (mode == "throw") throw new InvalidOperationException("enumeration");
                if (mode == "cancel") cancellation.Cancel();
                yield return mode == "null" ? null! : "second";
                visits++; yield return "must not visit";
            }
            finally { disposed = true; }
        }
        Action call = () => RepositoryCreateInputs.Prepare(Items(), mode == "limit" ? 1 : 3, cancellation.Token);
        call.Should().Throw<Exception>(); disposed.Should().BeTrue(); visits.Should().Be(2);
    }

    [Fact]
    public void NullInputAndPreCancellationFailBeforeEnumeration()
    {
        Action missing = () => RepositoryCreateInputs.Prepare<string>(null!, 3);
        missing.Should().Throw<ArgumentNullException>();
        var visited = false;
        IEnumerable<string> Items() { visited = true; yield return "never"; }
        Action canceled = () => RepositoryCreateInputs.Prepare(Items(), 3, new CancellationToken(true));
        canceled.Should().Throw<OperationCanceledException>(); visited.Should().BeFalse();
    }
}
