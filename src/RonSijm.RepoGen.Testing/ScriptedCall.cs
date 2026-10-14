namespace RonSijm.RepoGen.Testing;

/// <summary>A bounded call-history entry. Arguments are copied at invocation and again on inspection.</summary>
/// <remarks>The caller's snapshot adapter must actually detach mutable members. Results and exception objects are not retained.</remarks>
public sealed class ScriptedCall<TArguments>
{
    private readonly object gate = new();
    private readonly TArguments arguments;
    private readonly Func<TArguments, TArguments> snapshot;
    private ScriptedCallOutcome outcome;
    private string? errorType;
    private string? errorMessage;

    internal ScriptedCall(long sequence, TArguments arguments, Func<TArguments, TArguments> snapshot)
    {
        Sequence = sequence;
        this.snapshot = snapshot;
        this.arguments = snapshot(arguments);
    }

    public long Sequence { get; }
    public TArguments Arguments => snapshot(arguments);
    public ScriptedCallOutcome Outcome { get { lock (gate) return outcome; } }
    public string? ErrorType { get { lock (gate) return errorType; } }
    public string? ErrorMessage { get { lock (gate) return errorMessage; } }

    internal void Complete(ScriptedCallOutcome result, Exception? error)
    {
        lock (gate)
        {
            outcome = result;
            errorType = error?.GetType().FullName;
            errorMessage = error?.Message;
        }
    }
}
