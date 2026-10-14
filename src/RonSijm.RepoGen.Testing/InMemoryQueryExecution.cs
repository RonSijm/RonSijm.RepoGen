using System.Runtime.CompilerServices;

namespace RonSijm.RepoGen.Testing;

/// <summary>Cancellation-aware enumeration over detached fixture data, never a database query provider.</summary>
public static class InMemoryQueryExecution
{
    public static IEnumerable<T> WithCancellation<T>(IEnumerable<T> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public static async IAsyncEnumerable<T> Stream<T>(IEnumerable<T> items, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // No worker thread or artificial delays: retain a real cold async iterator for consumer cancellation/disposal.
        await Task.CompletedTask.ConfigureAwait(false);
        foreach (var item in WithCancellation(items, cancellationToken)) yield return item;
    }
}
