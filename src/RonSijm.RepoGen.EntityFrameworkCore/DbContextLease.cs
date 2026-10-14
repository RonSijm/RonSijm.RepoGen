using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Acquires a context asynchronously with explicit disposal ownership.</summary>
public interface IDbContextLeaseProvider<TContext> where TContext : DbContext
{
    ValueTask<DbContextLease<TContext>> AcquireAsync(CancellationToken cancellationToken = default);
}

/// <summary>Acquires an application-selected context without interpreting its key.</summary>
public interface IKeyedDbContextLeaseProvider<TContext, in TKey> where TContext : DbContext
{
    ValueTask<DbContextLease<TContext>> AcquireAsync(TKey key, CancellationToken cancellationToken = default);
}

/// <summary>A disposable lease. Borrowed leases never dispose their context.</summary>
public sealed class DbContextLease<TContext> : IAsyncDisposable where TContext : DbContext
{
    private readonly TContext context;
    private int disposed;

    internal DbContextLease(TContext context, bool ownsContext, string? scopeKey = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
        OwnsContext = ownsContext;
        if (scopeKey is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        }

        ScopeKey = scopeKey;
    }

    public TContext Context
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            return context;
        }
    }

    public bool OwnsContext { get; }

    /// <summary>Opaque application-defined cache partition, including database and visibility identity where relevant.</summary>
    public string? ScopeKey { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0 && OwnsContext)
        {
            await context.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>Creates leases with an explicit ownership choice.</summary>
public static class DbContextLease
{
    public static DbContextLease<TContext> Owned<TContext>(TContext context, string? scopeKey = null) where TContext : DbContext
        => new(context, ownsContext: true, scopeKey);

    public static DbContextLease<TContext> Borrowed<TContext>(TContext context, string? scopeKey = null) where TContext : DbContext
        => new(context, ownsContext: false, scopeKey);
}

/// <summary>Adapts a borrowed scoped context to the asynchronous lease contract.</summary>
public sealed class ScopedDbContextLeaseProvider<TContext> : IDbContextLeaseProvider<TContext>
    where TContext : DbContext
{
    private readonly TContext context;

    public ScopedDbContextLeaseProvider(TContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
    }

    public ValueTask<DbContextLease<TContext>> AcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(DbContextLease.Borrowed(context));
    }
}

/// <summary>Creates a new owned context for every operation using an EF context factory.</summary>
public sealed class FactoryDbContextLeaseProvider<TContext> : IDbContextLeaseProvider<TContext>
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> factory;

    public FactoryDbContextLeaseProvider(IDbContextFactory<TContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this.factory = factory;
    }

    public async ValueTask<DbContextLease<TContext>> AcquireAsync(CancellationToken cancellationToken = default)
        => DbContextLease.Owned(await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false));
}

/// <summary>
/// Binds an application-owned context key for an operation scope or an entire unit of work.
/// The key is fixed at construction; participating repositories cannot switch databases midway.
/// </summary>
public sealed class FixedKeyDbContextLeaseProvider<TContext, TKey> : IDbContextLeaseProvider<TContext>
    where TContext : DbContext
{
    private readonly IKeyedDbContextLeaseProvider<TContext, TKey> provider;
    private readonly TKey key;

    public FixedKeyDbContextLeaseProvider(IKeyedDbContextLeaseProvider<TContext, TKey> provider, TKey key)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(key);
        this.provider = provider;
        this.key = key;
    }

    public ValueTask<DbContextLease<TContext>> AcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return provider.AcquireAsync(key, cancellationToken);
    }
}