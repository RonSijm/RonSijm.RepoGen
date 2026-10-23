using System.Runtime.ExceptionServices;

namespace RonSijm.RepoGen.Testing.EntityFrameworkCore;

internal static class FixtureSessionExecution
{
    internal static async Task RunAsync(IAsyncDisposable session, Func<Task> action)
    {
        Exception? failure = null;
        try { await action().ConfigureAwait(false); }
        catch (Exception exception) { failure = exception; }
        try { await session.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception)
        {
            failure = failure is null ? exception : new AggregateException("Fixture operation and session cleanup both failed.", failure, exception);
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
