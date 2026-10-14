namespace RonSijm.RepoGen.EntityFrameworkCore.Tests;

public sealed class RelationalFixtureCleanupProbe : IAsyncDisposable
{
    public int Disposals { get; private set; }
    public Exception? Failure { get; set; }

    public ValueTask DisposeAsync()
    {
        Disposals++;
        return Failure is null ? ValueTask.CompletedTask : ValueTask.FromException(Failure);
    }
}
