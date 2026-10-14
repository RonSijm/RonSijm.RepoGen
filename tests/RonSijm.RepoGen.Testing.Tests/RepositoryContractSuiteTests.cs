using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class RepositoryContractSuiteTests
{
    [Fact]
    public async Task TheSameCasesCanRunOnDifferentBackendsWithFreshSessionsAndCleanup()
    {
        var sources = new List<RepositoryContractCase<object>>
        {
            new("first", (repository, _) => { repository.Should().BeOfType<object>(); return Task.CompletedTask; }),
            new("second", (_, _) => Task.CompletedTask)
        };
        var suite = new RepositoryContractSuite<object>(sources);
        sources.Clear();
        suite.CaseNames.Should().Equal("first", "second");
        for (var backend = 0; backend < 2; backend++)
        {
            var opened = 0;
            var closed = 0;
            await suite.RunAsync(_ =>
            {
                opened++;
                return ValueTask.FromResult(new RepositoryContractSession<object>(new object(), () => { closed++; return ValueTask.CompletedTask; }));
            });
            opened.Should().Be(2);
            closed.Should().Be(2);
        }
    }

    [Fact]
    public void CasesRejectNullCallbacksBlankNamesDuplicatesAndNullElements()
    {
        Action blank = () => _ = new RepositoryContractCase<object>(" ", (_, _) => Task.CompletedTask);
        Action callback = () => _ = new RepositoryContractCase<object>("read", null!);
        Action nullCases = () => _ = new RepositoryContractSuite<object>(null!);
        Action nullElement = () => _ = new RepositoryContractSuite<object>(new RepositoryContractCase<object>[] { null! });
        var item = new RepositoryContractCase<object>("read", (_, _) => Task.CompletedTask);
        Action duplicate = () => _ = new RepositoryContractSuite<object>(new[] { item, item });
        blank.Should().Throw<ArgumentException>();
        callback.Should().Throw<ArgumentNullException>();
        nullCases.Should().Throw<ArgumentNullException>();
        nullElement.Should().Throw<ArgumentNullException>();
        duplicate.Should().Throw<ArgumentException>().WithMessage("*Duplicate*read*");
    }

    [Fact]
    public async Task AssertionAndCleanupFailuresAreBothKeptWithCaseIdentityAndFailFast()
    {
        var assertionFailure = new IOException("assertion failed");
        var cleanupFailure = new IOException("cleanup failed");
        var suite = new RepositoryContractSuite<object>(new RepositoryContractCase<object>[]
        {
            new("named read", (_, _) => Task.FromException(assertionFailure)),
            new("must not run", (_, _) => throw new InvalidOperationException())
        });
        var calls = 0;
        Func<Task> run = () => suite.RunAsync(_ =>
        {
            calls++;
            return ValueTask.FromResult(new RepositoryContractSession<object>(new object(), () => ValueTask.FromException(cleanupFailure)));
        });
        var failure = (await run.Should().ThrowAsync<RepositoryContractFailureException>()).Which;
        failure.CaseName.Should().Be("named read");
        failure.Phase.Should().Be(RepositoryContractFailurePhase.Assertion);
        ((AggregateException)failure.InnerException!).InnerExceptions.Should().Equal(assertionFailure, cleanupFailure);
        calls.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpeningAndCleanupFailuresHaveTheirOwnPhase(bool opening)
    {
        var error = new IOException("operation failed");
        var suite = Suite((_, _) => Task.CompletedTask);
        Func<Task> run = () => suite.RunAsync(_ => opening
            ? ValueTask.FromException<RepositoryContractSession<object>>(error)
            : ValueTask.FromResult(new RepositoryContractSession<object>(new object(), () => ValueTask.FromException(error))));
        var failure = (await run.Should().ThrowAsync<RepositoryContractFailureException>()).Which;
        failure.Phase.Should().Be(opening ? RepositoryContractFailurePhase.OpenSession : RepositoryContractFailurePhase.Cleanup);
        failure.InnerException.Should().BeSameAs(error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationIsForwardedAndCleanupStillRuns(bool beforeOpening)
    {
        using var source = new CancellationTokenSource();
        var cleanup = 0;
        var opened = 0;
        var suite = Suite(async (_, token) =>
        {
            token.Should().Be(source.Token);
            await source.CancelAsync();
        });
        if (beforeOpening) await source.CancelAsync();
        Func<Task> run = () => suite.RunAsync(token =>
        {
            token.Should().Be(source.Token);
            opened++;
            return ValueTask.FromResult(new RepositoryContractSession<object>(new object(), () => { cleanup++; return ValueTask.CompletedTask; }));
        }, source.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
        opened.Should().Be(beforeOpening ? 0 : 1);
        cleanup.Should().Be(beforeOpening ? 0 : 1);
    }

    [Fact]
    public async Task CancellationWithCleanupFailureRetainsBothErrors()
    {
        using var source = new CancellationTokenSource();
        var suite = Suite(async (_, _) => await source.CancelAsync());
        Func<Task> run = () => suite.RunAsync(_ => ValueTask.FromResult(new RepositoryContractSession<object>(new object(),
            () => ValueTask.FromException(new IOException("cleanup")))), source.Token);
        var failure = (await run.Should().ThrowAsync<RepositoryContractFailureException>()).Which;
        ((AggregateException)failure.InnerException!).InnerExceptions.Should().Contain(exception => exception is OperationCanceledException)
            .And.Contain(exception => exception is IOException);
    }

    [Fact]
    public async Task CancellationDuringLastCleanupIsNotReportedAsSuiteSuccess()
    {
        using var source = new CancellationTokenSource();
        var suite = Suite((_, _) => Task.CompletedTask);
        Func<Task> run = () => suite.RunAsync(_ => ValueTask.FromResult(new RepositoryContractSession<object>(new object(),
            async () => await source.CancelAsync())), source.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NullSessionAndNullAssertionTaskFailClearly(bool nullSession)
    {
        var suite = Suite((_, _) => null!);
        Func<Task> run = () => suite.RunAsync(_ => ValueTask.FromResult(nullSession ? null! : new RepositoryContractSession<object>(new object())));
        var failure = (await run.Should().ThrowAsync<RepositoryContractFailureException>()).Which;
        failure.InnerException.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain(nullSession ? "factory returned null" : "null Task");
    }

    [Fact]
    public async Task SessionDisposalIsIdempotentSharesItsCompletionAndDoesNotInferRepositoryOwnership()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupCount = 0;
        var repository = new FixtureRow();
        var session = new RepositoryContractSession<FixtureRow>(repository, async () =>
        {
            cleanupCount++;
            await release.Task;
        });
        session.Repository.Should().BeSameAs(repository);
        var first = session.DisposeAsync().AsTask();
        var second = session.DisposeAsync().AsTask();
        second.Should().BeSameAs(first);
        second.IsCompleted.Should().BeFalse();
        Action access = () => _ = session.Repository;
        access.Should().Throw<ObjectDisposedException>();
        release.SetResult();
        await Task.WhenAll(first, second);
        cleanupCount.Should().Be(1);
        await new RepositoryContractSession<FixtureRow>(repository).DisposeAsync();
    }

    [Fact]
    public async Task ReentrantCleanupCannotUseTheAlreadyClosedSession()
    {
        RepositoryContractSession<object>? session = null;
        session = new(new object(), () =>
        {
            Action access = () => _ = session!.Repository;
            access.Should().Throw<ObjectDisposedException>();
            return ValueTask.CompletedTask;
        });
        await session.DisposeAsync();
    }

    private static RepositoryContractSuite<object> Suite(Func<object, CancellationToken, Task> assertion)
        => new(new[] { new RepositoryContractCase<object>("read", assertion) });
}
