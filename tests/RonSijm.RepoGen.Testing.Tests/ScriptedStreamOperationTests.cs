using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class ScriptedStreamOperationTests
{
    [Fact]
    public async Task StreamsAreColdAndEveryEnumerationIsASeparateInvocation()
    {
        var operation = Recorded().ReturnsItems([1, 2]);
        var stream = operation.InvokeStream(9);
        operation.InvocationCount.Should().Be(0);
        (await Read(stream)).Should().Equal(1, 2);
        (await Read(stream)).Should().Equal(1, 2);
        operation.InvocationCount.Should().Be(2);
        operation.Calls.Should().OnlyContain(call => call.Outcome == ScriptedCallOutcome.Completed && call.Arguments == 9);
    }

    [Fact]
    public async Task EarlyDisposalRecordsDisposedNotCompletedAndDisposesUnderlyingEnumerator()
    {
        var stream = new ProbeStream();
        var operation = Recorded();
        operation.Returns(stream);
        await using (var enumerator = operation.InvokeStream(1).GetAsyncEnumerator())
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            operation.Calls[0].Outcome.Should().Be(ScriptedCallOutcome.InProgress);
        }

        operation.Calls[0].Outcome.Should().Be(ScriptedCallOutcome.Disposed);
        stream.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task DisposingBeforeFirstMoveNextDoesNotInvokeTheScript()
    {
        var operation = Recorded();
        await using (operation.InvokeStream(1).GetAsyncEnumerator()) { }
        operation.InvocationCount.Should().Be(0);
        operation.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task NullStreamsFailExplicitly()
    {
        var operation = Recorded();
        operation.Returns(null!);
        Func<Task> read = () => Read(operation.InvokeStream(1));
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*returned null*");
        operation.Calls[0].Outcome.Should().Be(ScriptedCallOutcome.Failed);
    }

    [Theory]
    [InlineData("factory")]
    [InlineData("move")]
    [InlineData("current")]
    [InlineData("dispose")]
    public async Task StreamFailuresArePropagatedAndRecorded(string fault)
    {
        var stream = new ProbeStream(fault);
        var operation = Recorded();
        operation.Returns(stream);
        Func<Task> read = () => Read(operation.InvokeStream(2));
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage(fault);
        operation.Calls[0].Outcome.Should().Be(ScriptedCallOutcome.Failed);
        operation.Calls[0].ErrorMessage.Should().Be(fault);
        stream.Disposals.Should().Be(fault == "factory" ? 0 : 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothMethodAndEnumerationTokensAreHonored(bool methodToken)
    {
        using var call = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var stream = new ProbeStream();
        var operation = Recorded();
        operation.Returns(stream);
        await using var enumerator = operation.InvokeStream(3, call.Token).GetAsyncEnumerator(enumeration.Token);
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        stream.Token.CanBeCanceled.Should().BeTrue();
        if (methodToken) await call.CancelAsync();
        else await enumeration.CancelAsync();
        stream.Token.IsCancellationRequested.Should().BeTrue();
        Func<Task> next = async () => await enumerator.MoveNextAsync();
        await next.Should().ThrowAsync<OperationCanceledException>();
        operation.Calls[0].Outcome.Should().Be(ScriptedCallOutcome.Canceled);
        stream.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task DisposalFailureSupersedingIterationFailureIsTheRecordedException()
    {
        var operation = Recorded();
        operation.Returns(new ProbeStream("move-and-dispose"));
        Func<Task> read = () => Read(operation.InvokeStream(1));
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("dispose");
        operation.Calls[0].Outcome.Should().Be(ScriptedCallOutcome.Failed);
        operation.Calls[0].ErrorMessage.Should().Be("dispose");
    }

    [Fact]
    public async Task DisposalFailureSupersedingCancellationIsRecordedAsFailedNotCanceled()
    {
        using var source = new CancellationTokenSource();
        var operation = Recorded();
        operation.Returns(new ProbeStream("dispose"));
        await using var enumerator = operation.InvokeStream(1, source.Token).GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        await source.CancelAsync();
        Func<Task> next = async () => await enumerator.MoveNextAsync();
        await next.Should().ThrowAsync<InvalidOperationException>().WithMessage("dispose");
        operation.Calls[0].Outcome.Should().Be(ScriptedCallOutcome.Failed);
        operation.Calls[0].ErrorMessage.Should().Be("dispose");
    }

    [Fact]
    public async Task PreCanceledEnumerationDoesNotConsumeQueuedStreams()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var operation = Recorded();
        operation.EnqueueResult(new ProbeStream());
        Func<Task> read = () => Read(operation.InvokeStream(1), source.Token);
        await read.Should().ThrowAsync<OperationCanceledException>();
        operation.Calls[0].Outcome.Should().Be(ScriptedCallOutcome.Canceled);
        (await Read(operation.InvokeStream(2))).Should().Equal(1, 2);
    }

    [Fact]
    public async Task SuppliedItemsAreMaterializedAtArrangementTime()
    {
        var items = new List<int> { 1 };
        var operation = Recorded().ReturnsItems(items);
        items.Add(2);
        (await Read(operation.InvokeStream(0))).Should().Equal(1);
    }

    [Fact]
    public async Task AsyncStreamHandlerMustCompleteBeforeFirstItemAndUnarrangedStreamsFail()
    {
        var completion = new TaskCompletionSource<IAsyncEnumerable<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = Recorded();
        operation.Runs((_, _) => completion.Task);
        await using var enumerator = operation.InvokeStream(1).GetAsyncEnumerator();
        var pending = enumerator.MoveNextAsync();
        pending.IsCompleted.Should().BeFalse();
        operation.Calls[0].Outcome.Should().Be(ScriptedCallOutcome.InProgress);
        completion.SetResult(new ProbeStream());
        (await pending).Should().BeTrue();
        operation.Reset();
        Func<Task> read = () => Read(operation.InvokeStream(2));
        await read.Should().ThrowAsync<UnarrangedRepositoryOperationException>();
    }

    private static ScriptedStreamOperation<int, int> Recorded()
    {
        var operation = new ScriptedStreamOperation<int, int>("Stream");
        operation.RecordCalls(id => id);
        return operation;
    }

    private static async Task<IReadOnlyList<int>> Read(IAsyncEnumerable<int> stream, CancellationToken token = default)
    {
        var result = new List<int>();
        await foreach (var item in stream.WithCancellation(token)) result.Add(item);
        return result;
    }

    private sealed class ProbeStream(string? fault = null) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        private int current;
        internal int Disposals { get; private set; }
        internal CancellationToken Token { get; private set; }
        public int Current => fault == "current" ? throw new InvalidOperationException(fault) : current;
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            if (fault == "factory") throw new InvalidOperationException(fault);
            Token = cancellationToken;
            return this;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            if (fault is "move" or "move-and-dispose") throw new InvalidOperationException("move");
            current++;
            return ValueTask.FromResult(current <= 2);
        }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            if (fault is "dispose" or "move-and-dispose") throw new InvalidOperationException("dispose");
            return ValueTask.CompletedTask;
        }
    }
}
