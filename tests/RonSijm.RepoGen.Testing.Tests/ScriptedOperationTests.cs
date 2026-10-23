using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class ScriptedOperationTests
{
    [Fact]
    public async Task UnarrangedCallsFailAndDoNotInventEmptyResults()
    {
        var operation = new ScriptedOperation<int, string?>("Notes.FindAsync(int)");
        Func<Task> invoke = () => operation.InvokeAsync(1);
        (await invoke.Should().ThrowAsync<UnarrangedRepositoryOperationException>()).Which.Operation.Should().Be(operation.Operation);
        operation.InvocationCount.Should().Be(1);
        operation.Calls.Should().BeEmpty("recording is opt-in");
        operation.Returns(null);
        (await operation.InvokeAsync(1)).Should().BeNull("explicit null is a valid arrangement");
    }

    [Fact]
    public async Task QueuedMatchingResponsesAndFailuresAreConsumedOnceBeforeFallback()
    {
        var expected = new InvalidOperationException("retry me");
        var operation = new ScriptedOperation<int, string>("Find").Returns("fallback")
            .EnqueueException(expected, id => id == 2).EnqueueResult("second", id => id == 2).EnqueueResult("first", id => id == 1);
        (await operation.InvokeAsync(3)).Should().Be("fallback");
        (await operation.InvokeAsync(1)).Should().Be("first");
        Func<Task> fail = () => operation.InvokeAsync(2);
        (await fail.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(expected);
        (await operation.InvokeAsync(2)).Should().Be("second");
        (await operation.InvokeAsync(2)).Should().Be("fallback");
    }

    [Fact]
    public async Task PersistentRulesUseRegistrationOrderAndReceiveActualArgumentsAndToken()
    {
        using var source = new CancellationTokenSource();
        var operation = new ScriptedOperation<int, int>("Find").Returns(0)
            .When(id => id > 5, (id, token) =>
            {
                token.Should().Be(source.Token);
                return Task.FromResult(id * 2);
            }).When(_ => true, (_, _) => Task.FromResult(-1));
        (await operation.InvokeAsync(6, source.Token)).Should().Be(12);
        (await operation.InvokeAsync(7, source.Token)).Should().Be(14);
        (await operation.InvokeAsync(1)).Should().Be(-1);
    }

    [Fact]
    public async Task ExhaustedQueuesAreStrictWithoutFallback()
    {
        var operation = new ScriptedOperation<int, int>("Count").EnqueueResult(42);
        (await operation.InvokeAsync(0)).Should().Be(42);
        Func<Task> second = () => operation.InvokeAsync(0);
        await second.Should().ThrowAsync<UnarrangedRepositoryOperationException>();
    }

    [Fact]
    public async Task HistoryUsesExplicitSnapshotsOnCaptureAndInspectionAndIsBounded()
    {
        var operation = new ScriptedOperation<List<int>, int>("Save").Returns(1)
            .RecordCalls(items => items.ToList(), maximumCalls: 2);
        var arguments = new List<int> { 1 };
        await operation.InvokeAsync(arguments);
        arguments.Add(2);
        var call = operation.Calls[0];
        call.Arguments.Should().Equal(1);
        call.Arguments.Add(99);
        call.Arguments.Should().Equal(1);
        call.Outcome.Should().Be(ScriptedCallOutcome.Completed);
        call.ErrorType.Should().BeNull();
        await operation.InvokeAsync([2]);
        await operation.InvokeAsync([3]);
        operation.InvocationCount.Should().Be(3);
        operation.Calls.Select(item => item.Sequence).Should().Equal(2, 3);
        operation.Calls.SelectMany(item => item.Arguments).Should().Equal(2, 3);
    }

    [Fact]
    public async Task StopRecordingDoesNotClearPreviousCallsAndClearCallsPreservesArrangements()
    {
        var operation = new ScriptedOperation<int, int>("Find").Returns(8).RecordCalls(id => id);
        await operation.InvokeAsync(1);
        operation.StopRecording();
        await operation.InvokeAsync(2);
        operation.Calls.Should().ContainSingle().Which.Arguments.Should().Be(1);
        operation.InvocationCount.Should().Be(2);
        operation.ClearCalls();
        operation.Calls.Should().BeEmpty();
        operation.InvocationCount.Should().Be(0);
        (await operation.InvokeAsync(3)).Should().Be(8);
        operation.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ResetClearsScriptsAndCountersButKeepsRecordingPolicy()
    {
        var operation = new ScriptedOperation<int, int>("Find").Returns(8).RecordCalls(id => id);
        await operation.InvokeAsync(1);
        operation.Reset();
        operation.InvocationCount.Should().Be(0);
        operation.Calls.Should().BeEmpty();
        Func<Task> invoke = () => operation.InvokeAsync(3);
        await invoke.Should().ThrowAsync<UnarrangedRepositoryOperationException>();
        operation.Calls.Should().ContainSingle().Which.Outcome.Should().Be(ScriptedCallOutcome.Failed);
        operation.Calls[0].Sequence.Should().Be(1);
    }

    [Fact]
    public async Task RealAsyncHandlersRemainInProgressAndFinishWithRecordedOutcome()
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new ScriptedOperation<int, int>("Wait").Runs((_, _) => completion.Task).RecordCalls(id => id);
        var pending = operation.InvokeAsync(4);
        pending.IsCompleted.Should().BeFalse();
        var call = operation.Calls.Should().ContainSingle().Which;
        call.Outcome.Should().Be(ScriptedCallOutcome.InProgress);
        completion.SetResult(7);
        (await pending).Should().Be(7);
        call.Outcome.Should().Be(ScriptedCallOutcome.Completed);
    }

    [Fact]
    public async Task SynchronousHandlerAndMatcherExceptionsAreRecordedWithoutRetainingExceptionObjects()
    {
        var operation = new ScriptedOperation<int, int>("Fail").Runs((_, _) => throw new InvalidOperationException("handler"))
            .RecordCalls(id => id);
        Func<Task> invoke = () => operation.InvokeAsync(2);
        await invoke.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler");
        operation.Calls[0].ErrorType.Should().Be(typeof(InvalidOperationException).FullName);
        operation.Calls[0].ErrorMessage.Should().Be("handler");
        operation.Calls[0].Outcome.Should().Be(ScriptedCallOutcome.Failed);
        operation.When(_ => throw new ArgumentException("matcher"), (_, _) => Task.FromResult(1));
        await invoke.Should().ThrowAsync<ArgumentException>().WithMessage("matcher");
        operation.Calls[1].Outcome.Should().Be(ScriptedCallOutcome.Failed);
    }

    [Fact]
    public async Task PreCanceledCallsAreRecordedButDoNotConsumeQueuedResults()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var operation = new ScriptedOperation<int, int>("Find").EnqueueResult(5).RecordCalls(id => id);
        Func<Task> canceled = () => operation.InvokeAsync(1, source.Token);
        (await canceled.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(source.Token);
        operation.Calls[0].Outcome.Should().Be(ScriptedCallOutcome.Canceled);
        (await operation.InvokeAsync(2)).Should().Be(5);
        operation.InvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task CancellationIsCheckedAfterAHandlerThatIgnoresTheToken()
    {
        using var source = new CancellationTokenSource();
        var operation = new ScriptedOperation<int, int>("Find").Runs(async (_, _) =>
        {
            await source.CancelAsync();
            return 1;
        }).RecordCalls(id => id);
        Func<Task> invoke = () => operation.InvokeAsync(1, source.Token);
        await invoke.Should().ThrowAsync<OperationCanceledException>();
        operation.Calls[0].Outcome.Should().Be(ScriptedCallOutcome.Canceled);
    }

    [Fact]
    public async Task ParallelInvocationsConsumeEachQueueEntryExactlyOnce()
    {
        var operation = new ScriptedOperation<int, int>("Find").RecordCalls(id => id, maximumCalls: 50);
        for (var item = 0; item < 50; item++) operation.EnqueueResult(item);
        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(id => Task.Run(() => operation.InvokeAsync(id))));
        results.Order().Should().Equal(Enumerable.Range(0, 50));
        operation.InvocationCount.Should().Be(50);
        operation.Calls.Select(call => call.Sequence).Should().Equal(Enumerable.Range(1, 50).Select(item => (long)item));
        operation.Calls.Should().OnlyContain(call => call.Outcome == ScriptedCallOutcome.Completed);
    }

    [Fact]
    public async Task ResetDoesNotResurrectHistoryForAnInFlightHandler()
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new ScriptedOperation<int, int>("Find").Runs((_, _) => completion.Task).RecordCalls(id => id);
        var pending = operation.InvokeAsync(1);
        var oldCall = operation.Calls[0];
        operation.Reset();
        completion.SetResult(9);
        (await pending).Should().Be(9);
        oldCall.Outcome.Should().Be(ScriptedCallOutcome.Completed);
        operation.Calls.Should().BeEmpty();
        operation.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task ResetDuringSnapshotCannotRepopulateClearedHistory()
    {
        var operation = new ScriptedOperation<int, int>("Find").Returns(7);
        operation.RecordCalls(id => { operation.ClearCalls(); return id; });
        (await operation.InvokeAsync(1)).Should().Be(7);
        operation.Calls.Should().BeEmpty();
        operation.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task FailedSnapshotDoesNotConsumeAScriptOrFabricateHistory()
    {
        var operation = new ScriptedOperation<int, int>("Find").EnqueueResult(3).RecordCalls(_ => throw new InvalidOperationException("copy"));
        Func<Task> invoke = () => operation.InvokeAsync(1);
        await invoke.Should().ThrowAsync<InvalidOperationException>().WithMessage("copy");
        operation.InvocationCount.Should().Be(1);
        operation.Calls.Should().BeEmpty();
        operation.StopRecording();
        (await operation.InvokeAsync(1)).Should().Be(3);
    }

    [Fact]
    public async Task NullTaskHandlersFailWithAnOperationSpecificDiagnostic()
    {
        var operation = new ScriptedOperation<int, int>("InvalidHandler").Runs((_, _) => null!).RecordCalls(id => id);
        Func<Task> invoke = () => operation.InvokeAsync(1);
        await invoke.Should().ThrowAsync<InvalidOperationException>().WithMessage("*null Task*InvalidHandler*");
        operation.Calls[0].Outcome.Should().Be(ScriptedCallOutcome.Failed);
    }

    [Fact]
    public void InvalidConfigurationFailsAtArrangementTime()
    {
        Action emptyName = () => _ = new ScriptedOperation<int, int>(" ");
        emptyName.Should().Throw<ArgumentException>();
        var operation = new ScriptedOperation<int, int>("Find");
        Action handler = () => operation.Runs(null!);
        Action matcher = () => operation.When(null!, (_, _) => Task.FromResult(1));
        Action snapshot = () => operation.RecordCalls(null!);
        Action capacity = () => operation.RecordCalls(id => id, 0);
        Action exception = () => operation.Throws(null!);
        handler.Should().Throw<ArgumentNullException>();
        matcher.Should().Throw<ArgumentNullException>();
        snapshot.Should().Throw<ArgumentNullException>();
        capacity.Should().Throw<ArgumentOutOfRangeException>();
        exception.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RuntimeReferencesOnlyEfFreeRepoGenAbstractionsNotOrmOrGeneratorAssemblies()
    {
        typeof(ScriptedOperation<,>).Assembly.GetReferencedAssemblies().Select(name => name.Name).Should()
            .NotContain(name => name != null && (name.Contains("EntityFramework", StringComparison.Ordinal) ||
                name.StartsWith("RonSijm.RepoGen", StringComparison.Ordinal) && name != "RonSijm.RepoGen.Abstractions"));
        typeof(RonSijm.RepoGen.Abstractions.IRepositoryUnitOfWork).Assembly.GetReferencedAssemblies().Select(name => name.Name).Should()
            .NotContain(name => name != null && (name.Contains("EntityFramework", StringComparison.Ordinal) || name.StartsWith("RonSijm.RepoGen", StringComparison.Ordinal)));
    }
}
