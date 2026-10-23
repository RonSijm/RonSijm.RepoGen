using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace RonSijm.RepoGen.Testing.EntityFrameworkCore;

/// <summary>Owns explicitly configured real-provider DI sessions; never infers database deletion or fake SQL behavior.</summary>
public sealed class RelationalRepositoryFixture<TContext> : IAsyncDisposable where TContext : DbContext
{
    private readonly object gate = new();
    private readonly ServiceProvider provider;
    private readonly Func<TContext, CancellationToken, Task>? seed;
    private readonly Func<TContext, CancellationToken, Task>? reset;
    private readonly HashSet<RelationalRepositorySession<TContext>> sessions = new();
    private Task? disposal;
    private bool resetting;

    private RelationalRepositoryFixture(ServiceProvider provider, Func<TContext, CancellationToken, Task>? seed,
        Func<TContext, CancellationToken, Task>? reset)
    {
        this.provider = provider;
        this.seed = seed;
        this.reset = reset;
    }

    public int ActiveSessions { get { lock (gate) return sessions.Count; } }

    internal static async Task<RelationalRepositoryFixture<TContext>> CreateAsync(
        Action<RelationalRepositoryFixtureOptions<TContext>> configure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        cancellationToken.ThrowIfCancellationRequested();
        var options = new RelationalRepositoryFixtureOptions<TContext>();
        configure(options);
        var registration = options.ConfigureServices
            ?? throw new InvalidOperationException("A relational fixture requires explicit ConfigureServices/provider registration.");
        var initialize = options.InitializeDatabaseAsync;
        var seed = options.SeedAsync;
        var reset = options.ResetDatabaseAsync;
        var services = new ServiceCollection();
        registration(services);
        var context = services.LastOrDefault(static service => service.ServiceType == typeof(TContext));
        if (context is null || context.Lifetime != ServiceLifetime.Scoped)
        {
            throw new InvalidOperationException("Register the fixture DbContext as scoped, never singleton or transient; repositories in a session must share the same context.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var fixture = new RelationalRepositoryFixture<TContext>(provider, seed, reset);
        try
        {
            var session = await fixture.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
            await FixtureSessionExecution.RunAsync(session, async () =>
            {
                if (initialize is not null)
                {
                    await initialize(session.Context, cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (seed is not null)
                {
                    await seed(session.Context, cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
            }).ConfigureAwait(false);
            return fixture;
        }
        catch (Exception failure)
        {
            await DisposeAfterFailureAsync(fixture, failure).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Opens a fresh scope and verifies it resolves a relational context. Does not seed, save or begin a transaction.</summary>
    public async ValueTask<RelationalRepositorySession<TContext>> OpenSessionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = CreateSession(duringReset: false);
        try
        {
            ValidateProvider(session.Context);
            cancellationToken.ThrowIfCancellationRequested();
            return session;
        }
        catch (Exception failure)
        {
            await DisposeAfterFailureAsync(session, failure).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Runs the explicit reset callback, then seeds in a fresh scope. Refuses active sessions or another reset.</summary>
    /// <remarks>No implicit transaction/retry/schema deletion. A callback failure may leave provider-owned state partially reset.</remarks>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposal is not null, this);
            if (reset is null)
            {
                throw new InvalidOperationException("No ResetDatabaseAsync callback is configured; the fixture will not infer database deletion/reset.");
            }
            if (resetting || sessions.Count != 0)
            {
                throw new InvalidOperationException("Close all fixture sessions before resetting; concurrent resets are not supported.");
            }
            resetting = true;
        }
        try
        {
            var session = CreateSession(duringReset: true);
            await FixtureSessionExecution.RunAsync(session, async () =>
            {
                ValidateProvider(session.Context);
                await reset(session.Context, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (seed is not null)
                {
                    await seed(session.Context, cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
            }).ConfigureAwait(false);
        }
        finally
        {
            lock (gate) resetting = false;
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task pending;
        lock (gate)
        {
            if (resetting)
            {
                throw new InvalidOperationException("Await fixture reset before disposing the fixture.");
            }
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

    private RelationalRepositorySession<TContext> CreateSession(bool duringReset)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposal is not null, this);
            if (resetting && !duringReset)
            {
                throw new InvalidOperationException("Cannot open a fixture session while resetting.");
            }
            var session = new RelationalRepositorySession<TContext>(provider.CreateAsyncScope(), Release);
            sessions.Add(session);
            return session;
        }
    }

    private void Release(RelationalRepositorySession<TContext> session)
    {
        lock (gate) sessions.Remove(session);
    }

    private static void ValidateProvider(TContext context)
    {
        if (!context.Database.IsRelational())
        {
            throw new InvalidOperationException("A relational repository fixture requires a real relational provider, not EF InMemory.");
        }
    }

    private async Task DisposeCoreAsync(RelationalRepositorySession<TContext>[] active)
    {
        var failures = new List<Exception>();
        foreach (var session in active)
        {
            try { await session.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) { failures.Add(failure); }
        }
        try { await provider.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure) { failures.Add(failure); }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Relational fixture cleanup failed; every session and provider cleanup was attempted.", failures);
    }

    private async Task CompleteDisposalAsync(TaskCompletionSource completion)
    {
        RelationalRepositorySession<TContext>[] active;
        lock (gate) active = sessions.ToArray();
        try
        {
            await DisposeCoreAsync(active).ConfigureAwait(false);
            completion.SetResult();
        }
        catch (Exception failure) { completion.SetException(failure); }
    }

    private static async Task DisposeAfterFailureAsync(IAsyncDisposable owned, Exception failure)
    {
        try { await owned.DisposeAsync().ConfigureAwait(false); }
        catch (Exception cleanup) { throw new AggregateException("Fixture setup and cleanup both failed.", failure, cleanup); }
    }
}
