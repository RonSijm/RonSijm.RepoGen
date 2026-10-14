namespace RonSijm.RepoGen.Testing;

/// <summary>An application port and its explicitly owned per-case cleanup.</summary>
/// <remarks>Does not infer ownership of the repository. Supply the scope/fixture cleanup explicitly.</remarks>
public sealed class RepositoryContractSession<TRepository> : IAsyncDisposable where TRepository : class
{
    private readonly object gate = new();
    private readonly TRepository repository;
    private readonly Func<ValueTask>? cleanup;
    private Task? disposal;

    public RepositoryContractSession(TRepository repository, Func<ValueTask>? cleanup = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        this.repository = repository;
        this.cleanup = cleanup;
    }

    public TRepository Repository
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposal is not null, this);
                return repository;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task pending;
        lock (gate)
        {
            if (disposal is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = completion.Task;
            }
            pending = disposal;
        }
        if (completion is not null) _ = CompleteDisposalAsync(completion);
        return new(pending);
    }

    private async Task DisposeCoreAsync()
    {
        if (cleanup is not null)
        {
            await cleanup().ConfigureAwait(false);
        }
    }

    private async Task CompleteDisposalAsync(TaskCompletionSource completion)
    {
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
            completion.SetResult();
        }
        catch (Exception failure) { completion.SetException(failure); }
    }
}
