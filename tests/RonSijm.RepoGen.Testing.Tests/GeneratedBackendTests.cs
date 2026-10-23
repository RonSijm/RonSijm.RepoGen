using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class GeneratedBackendTests
{
    [Fact]
    public async Task ExplicitRulesAndHandlerOverrideBackendAndResetRestoresIt()
    {
        var executions = 0;
        var operation = new ScriptedOperation<int, int>("Read", (value, _) => { executions++; return Task.FromResult(value * 2); });
        operation.RecordCalls(value => value);
        operation.EnqueueResult(99, value => value == 1).When(value => value == 2, (_, _) => Task.FromResult(42));
        (await operation.InvokeAsync(3)).Should().Be(6);
        (await operation.InvokeAsync(1)).Should().Be(99);
        (await operation.InvokeAsync(1)).Should().Be(2);
        (await operation.InvokeAsync(2)).Should().Be(42);
        operation.Returns(-1);
        (await operation.InvokeAsync(3)).Should().Be(-1);
        (await operation.InvokeAsync(2)).Should().Be(42);
        executions.Should().Be(2);
        operation.Reset();
        operation.InvocationCount.Should().Be(0);
        operation.Calls.Should().BeEmpty();
        (await operation.InvokeAsync(2)).Should().Be(4);
        operation.Calls.Should().ContainSingle().Which.Outcome.Should().Be(ScriptedCallOutcome.Completed);
    }

    [Fact]
    public async Task CancellationDoesNotExecuteBackendOrConsumeOverride()
    {
        var executions = 0;
        var operation = new ScriptedOperation<int, int>("Read", (_, _) => { executions++; return Task.FromResult(1); }).EnqueueResult(2);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        Func<Task> call = () => operation.InvokeAsync(0, canceled.Token);
        await call.Should().ThrowAsync<OperationCanceledException>();
        executions.Should().Be(0);
        (await operation.InvokeAsync(0)).Should().Be(2);
        (await operation.InvokeAsync(0)).Should().Be(1);
    }

    [Fact]
    public async Task UnsupportedBackendHasIdentityAndCanBeOverriddenWithoutChangingItsReceipt()
    {
        var operation = new ScriptedOperation<int, int>("Report", (_, _) => throw new UnsupportedInMemoryRepositoryOperationException("Report", "SQL required"));
        Func<Task> call = () => operation.InvokeAsync(0);
        var failure = (await call.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>()).Which;
        failure.Operation.Should().Be("Report");
        failure.Reason.Should().Be("SQL required");
        operation.EnqueueResult(3);
        (await operation.InvokeAsync(0)).Should().Be(3);
        await call.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
    }

    [Fact]
    public async Task StreamBackendIsColdReenumerableAndRetainedAfterReset()
    {
        var snapshots = 0;
        var operation = new ScriptedStreamOperation<int, int>("Feed", (value, token) =>
        {
            snapshots++;
            return Task.FromResult(InMemoryQueryExecution.Stream(new[] { value }, token));
        });
        var stream = operation.InvokeStream(5);
        snapshots.Should().Be(0);
        foreach (var iteration in new[] { 1, 2 })
        {
            await foreach (var value in stream) value.Should().Be(5);
            snapshots.Should().Be(iteration);
        }
        operation.ReturnsItems([99]);
        await foreach (var value in operation.InvokeStream(5)) value.Should().Be(99);
        operation.Reset();
        await foreach (var value in stream) value.Should().Be(5);
        snapshots.Should().Be(3);
    }

    [Fact]
    public void NullBackendIsRejectedForBothOperationKinds()
    {
        Action task = () => _ = new ScriptedOperation<int, int>("Read", null!);
        Action stream = () => _ = new ScriptedStreamOperation<int, int>("Feed", null!);
        task.Should().Throw<ArgumentNullException>();
        stream.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CancellationAwareEnumerableAndStreamCheckBetweenItems()
    {
        using var canceled = new CancellationTokenSource();
        var values = new[] { 1, 2 };
        using var iterator = InMemoryQueryExecution.WithCancellation(values, canceled.Token).GetEnumerator();
        iterator.MoveNext().Should().BeTrue();
        await canceled.CancelAsync();
        Action next = () => iterator.MoveNext();
        next.Should().Throw<OperationCanceledException>();
        await using var stream = InMemoryQueryExecution.Stream(values, canceled.Token).GetAsyncEnumerator();
        Func<Task> move = async () => await stream.MoveNextAsync();
        await move.Should().ThrowAsync<OperationCanceledException>();
    }
}
