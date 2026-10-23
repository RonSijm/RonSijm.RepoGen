using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace RonSijm.RepoGen.Testing.EntityFrameworkCore;

/// <summary>One DI scope containing the real context and its generated/application repositories.</summary>
/// <remarks>The context is test infrastructure, not a service-side query API. A session is not a transaction;
/// save/transaction behavior belongs to the real repository/unit of work. Do not use one DbContext concurrently.</remarks>
public sealed class RelationalRepositorySession<TContext> : IAsyncDisposable where TContext : DbContext
{
    private readonly object gate = new();
    private readonly AsyncServiceScope scope;
    private readonly Action<RelationalRepositorySession<TContext>> released;
    private Task? disposal;

    internal RelationalRepositorySession(AsyncServiceScope scope, Action<RelationalRepositorySession<TContext>> released)
    {
        this.scope = scope;
        this.released = released;
    }

    public IServiceProvider Services
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposal is not null, this);
                return scope.ServiceProvider;
            }
        }
    }

    public TContext Context => Services.GetRequiredService<TContext>();
    public TRepository GetRepository<TRepository>() where TRepository : class => Services.GetRequiredService<TRepository>();

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
        try
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            released(this);
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
