namespace RonSijm.RepoGen.Testing;

/// <summary>A typed operation script. Matching rules run in registration order; queued rules are consumed once.</summary>
/// <remarks>Recording is off until an explicit argument snapshot adapter is supplied. Handlers and matchers run outside locks.
/// Arrange before exercising the service; Reset does not cancel in-flight handlers or resurrect their cleared history.</remarks>
public class ScriptedOperation<TArguments, TResult>
{
    private readonly object gate = new();
    private readonly List<ScriptedRule<TArguments, TResult>> rules = new();
    private readonly List<ScriptedCall<TArguments>> calls = new();
    private Func<TArguments, CancellationToken, Task<TResult>>? fallback;
    private readonly Func<TArguments, CancellationToken, Task<TResult>>? defaultHandler;
    private Func<TArguments, TArguments>? snapshot;
    private int maximumCalls;
    private long invocationCount;
    private long historyEpoch;

    public ScriptedOperation(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        Operation = operation;
    }

    /// <summary>Uses a generated backend when no explicit arrangement matches. Reset retains this backend.</summary>
    public ScriptedOperation(string operation, Func<TArguments, CancellationToken, Task<TResult>> defaultHandler) : this(operation)
    {
        ArgumentNullException.ThrowIfNull(defaultHandler);
        this.defaultHandler = defaultHandler;
    }

    public string Operation { get; }
    public long InvocationCount { get { lock (gate) return invocationCount; } }
    public IReadOnlyList<ScriptedCall<TArguments>> Calls { get { lock (gate) return Array.AsReadOnly(calls.ToArray()); } }

    public ScriptedOperation<TArguments, TResult> Returns(TResult result) => Runs((_, _) => Task.FromResult(result));

    public ScriptedOperation<TArguments, TResult> Throws(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Runs((_, _) => Task.FromException<TResult>(exception));
    }

    public ScriptedOperation<TArguments, TResult> Runs(Func<TArguments, CancellationToken, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (gate) fallback = handler;
        return this;
    }

    public ScriptedOperation<TArguments, TResult> When(Func<TArguments, bool> matches, Func<TArguments, CancellationToken, Task<TResult>> handler)
        => Add(matches, handler, once: false);

    public ScriptedOperation<TArguments, TResult> Enqueue(Func<TArguments, CancellationToken, Task<TResult>> handler, Func<TArguments, bool>? when = null)
        => Add(when ?? (_ => true), handler, once: true);

    public ScriptedOperation<TArguments, TResult> EnqueueResult(TResult result, Func<TArguments, bool>? when = null)
        => Enqueue((_, _) => Task.FromResult(result), when);

    public ScriptedOperation<TArguments, TResult> EnqueueException(Exception exception, Func<TArguments, bool>? when = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Enqueue((_, _) => Task.FromException<TResult>(exception), when);
    }

    /// <summary>Enables bounded history. The adapter must copy nested mutable data; it is used for both capture and inspection.</summary>
    public ScriptedOperation<TArguments, TResult> RecordCalls(Func<TArguments, TArguments> snapshotArguments, int maximumCalls = 100)
    {
        ArgumentNullException.ThrowIfNull(snapshotArguments);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCalls, 1);
        lock (gate)
        {
            snapshot = snapshotArguments;
            this.maximumCalls = maximumCalls;
            TrimHistory();
        }

        return this;
    }

    public void StopRecording()
    {
        lock (gate) snapshot = null;
    }

    /// <summary>Clears scripts, counts and history; retains the generated backend and explicit recording/copy policy.</summary>
    public void Reset()
    {
        lock (gate)
        {
            rules.Clear();
            fallback = null;
            ClearHistory();
        }
    }

    /// <summary>Clears counts/history without consuming or changing arrangements.</summary>
    public void ClearCalls()
    {
        lock (gate) ClearHistory();
    }

    public async Task<TResult> InvokeAsync(TArguments arguments, CancellationToken cancellationToken = default)
    {
        var invocation = Begin(arguments);
        var result = await ExecuteAsync(invocation, arguments, cancellationToken).ConfigureAwait(false);
        invocation.Complete(ScriptedCallOutcome.Completed);
        return result;
    }

    private protected ScriptedInvocation<TArguments> Begin(TArguments arguments)
    {
        Func<TArguments, TArguments>? copy;
        long sequence;
        long epoch;
        lock (gate)
        {
            sequence = ++invocationCount;
            copy = snapshot;
            epoch = historyEpoch;
        }

        // Never execute consumer copy code under the configuration/history lock.
        var call = copy is null ? null : new ScriptedCall<TArguments>(sequence, arguments, copy);
        if (call is not null)
        {
            lock (gate)
            {
                if (epoch == historyEpoch)
                {
                    calls.Add(call);
                    calls.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
                    TrimHistory();
                }
            }
        }

        return new(call);
    }

    private protected async Task<TResult> ExecuteAsync(ScriptedInvocation<TArguments> invocation, TArguments arguments, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var handler = Select(arguments);
            var task = handler(arguments, token) ?? throw new InvalidOperationException("A scripted handler returned a null Task: " + Operation);
            var result = await task.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception exception)
        {
            invocation.Fail(exception);
            throw;
        }
    }

    private ScriptedOperation<TArguments, TResult> Add(Func<TArguments, bool> matches, Func<TArguments, CancellationToken, Task<TResult>> handler, bool once)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(handler);
        lock (gate) rules.Add(new(matches, handler, once));
        return this;
    }

    private Func<TArguments, CancellationToken, Task<TResult>> Select(TArguments arguments)
    {
        ScriptedRule<TArguments, TResult>[] candidates;
        lock (gate) candidates = rules.ToArray();
        foreach (var candidate in candidates)
        {
            if (!candidate.Matches(arguments)) continue;
            lock (gate)
            {
                if (!rules.Contains(candidate)) continue;
                if (candidate.Once) rules.Remove(candidate);
                return candidate.Handler;
            }
        }

        lock (gate) return fallback ?? defaultHandler ?? throw new UnarrangedRepositoryOperationException(Operation);
    }

    private void TrimHistory()
    {
        if (calls.Count > maximumCalls) calls.RemoveRange(0, calls.Count - maximumCalls);
    }

    private void ClearHistory()
    {
        calls.Clear();
        invocationCount = 0;
        historyEpoch++;
    }
}
