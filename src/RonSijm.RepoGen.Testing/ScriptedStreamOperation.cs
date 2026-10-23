using System.Runtime.CompilerServices;

namespace RonSijm.RepoGen.Testing;

/// <summary>A cold scripted stream: each enumeration is an invocation, completed only when exhausted and disposed.</summary>
public sealed class ScriptedStreamOperation<TArguments, TItem> : ScriptedOperation<TArguments, IAsyncEnumerable<TItem>>
{
    public ScriptedStreamOperation(string operation) : base(operation) { }

    public ScriptedStreamOperation(string operation, Func<TArguments, CancellationToken, Task<IAsyncEnumerable<TItem>>> defaultHandler)
        : base(operation, defaultHandler) { }

    public ScriptedStreamOperation<TArguments, TItem> ReturnsItems(IEnumerable<TItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var supplied = items.ToArray();
        Returns(Enumerate(supplied));
        return this;
    }

    public async IAsyncEnumerable<TItem> InvokeStream(TArguments arguments, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var invocation = Begin(arguments);
        IAsyncEnumerator<TItem>? enumerator = null;
        var exhausted = false;
        try
        {
            var stream = await ExecuteAsync(invocation, arguments, cancellationToken).ConfigureAwait(false);
            try
            {
                enumerator = (stream ?? throw new InvalidOperationException("A scripted stream handler returned null: " + Operation)).GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception exception)
            {
                invocation.Fail(exception);
                throw;
            }

            while (true)
            {
                bool more;
                TItem item;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    more = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    item = more ? enumerator.Current : default!;
                }
                catch (Exception exception)
                {
                    invocation.Fail(exception);
                    throw;
                }

                if (!more)
                {
                    exhausted = true;
                    break;
                }

                yield return item;
            }
        }
        finally
        {
            await DisposeAsync(enumerator, invocation).ConfigureAwait(false);
            invocation.Complete(exhausted ? ScriptedCallOutcome.Completed : ScriptedCallOutcome.Disposed);
        }
    }

    private static async ValueTask DisposeAsync(IAsyncEnumerator<TItem>? enumerator, ScriptedInvocation<TArguments> invocation)
    {
        try
        {
            if (enumerator is not null) await enumerator.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            invocation.Fail(exception);
            throw;
        }
    }

    private static async IAsyncEnumerable<TItem> Enumerate(IEnumerable<TItem> items, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }
}
