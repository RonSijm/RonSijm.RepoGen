namespace RonSijm.RepoGen.Testing;

internal sealed class ScriptedRule<TArguments, TResult>
{
    internal ScriptedRule(Func<TArguments, bool> matches, Func<TArguments, CancellationToken, Task<TResult>> handler, bool once)
    {
        Matches = matches;
        Handler = handler;
        Once = once;
    }

    internal Func<TArguments, bool> Matches { get; }
    internal Func<TArguments, CancellationToken, Task<TResult>> Handler { get; }
    internal bool Once { get; }
}
