using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Testing;

internal sealed class InMemoryRepositoryTransaction(InMemoryWriteDataContext context, Action restore) : IRepositoryTransaction
{
    private Action? checkpoint = restore;
    internal void Restore() => checkpoint?.Invoke();
    internal void Complete() => checkpoint = null;

    public Task CommitAsync(CancellationToken cancellationToken = default) => Finish(false, cancellationToken);
    public Task RollbackAsync(CancellationToken cancellationToken = default) => Finish(true, cancellationToken);
    private Task Finish(bool rollback, CancellationToken cancellationToken)
    {
        try { context.CompleteTransaction(this, rollback, cancellationToken); return Task.CompletedTask; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Task.FromCanceled(cancellationToken); }
        catch (Exception exception) { return Task.FromException(exception); }
    }
    public async ValueTask DisposeAsync()
    {
        if (checkpoint is not null) await RollbackAsync().ConfigureAwait(false);
    }
}
