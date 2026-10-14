namespace RonSijm.RepoGen.Testing;

internal sealed class ScriptedInvocation<TArguments>
{
    private readonly ScriptedCall<TArguments>? call;

    internal ScriptedInvocation(ScriptedCall<TArguments>? call) => this.call = call;

    internal ScriptedCallOutcome Outcome { get; private set; }

    internal void Complete(ScriptedCallOutcome outcome, Exception? error = null)
    {
        if (Outcome != ScriptedCallOutcome.InProgress) return;
        Outcome = outcome;
        call?.Complete(outcome, error);
    }

    internal void Fail(Exception error)
    {
        // A disposal failure can replace an iteration/cancellation exception, just as in a normal async iterator.
        // Record the failure actually escaping to the caller, not an earlier exception that cleanup superseded.
        Outcome = error is OperationCanceledException ? ScriptedCallOutcome.Canceled : ScriptedCallOutcome.Failed;
        call?.Complete(Outcome, error);
    }
}
