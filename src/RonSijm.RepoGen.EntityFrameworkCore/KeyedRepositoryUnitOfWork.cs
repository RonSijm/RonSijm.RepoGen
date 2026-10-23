using System.Data;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>A unit of work bound by its first request. Later requests cannot switch context keys.</summary>
/// <remarks>Bind by acquiring a lease before beginning a transaction. Like DbContext, do not use concurrently.</remarks>
public sealed class KeyedRepositoryUnitOfWork<TContext, TKey> : IKeyedDbContextLeaseProvider<TContext, TKey>, IRepositoryUnitOfWork, IRepositoryCommitSource, IRepositorySaveParticipantSource<TContext>
    where TContext : DbContext
{
    private readonly IKeyedDbContextLeaseProvider<TContext, TKey> provider;
    private readonly IRepositoryCommitObserver[] observers;
    private readonly Dictionary<object, IRepositoryCommitObserver> registered = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, IRepositorySaveParticipant<TContext>> saveParticipants = new(ReferenceEqualityComparer.Instance);
    private RepositoryUnitOfWork<TContext>? work;
    private TKey? key;
    private bool disposed;

    public KeyedRepositoryUnitOfWork(IKeyedDbContextLeaseProvider<TContext, TKey> provider, IEnumerable<IRepositoryCommitObserver>? observers = null)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.observers = observers?.ToArray() ?? [];
    }

    public ValueTask<DbContextLease<TContext>> AcquireAsync(TKey key, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        if (work is null)
        {
            this.key = key;
            work = new(new FixedKeyDbContextLeaseProvider<TContext, TKey>(provider, key), observers);
            foreach (var observer in registered)
            {
                work.RegisterCommitObserver(observer.Key, observer.Value);
            }

            foreach (var participant in saveParticipants)
            {
                work.RegisterSaveParticipant(participant.Key, participant.Value);
            }
        }
        else if (!EqualityComparer<TKey>.Default.Equals(this.key, key))
        {
            throw new InvalidOperationException("A shared unit of work cannot switch context keys. Start a new unit of work for the other key.");
        }

        return work.AcquireAsync(cancellationToken);
    }

    public void RegisterCommitObserver(object identity, IRepositoryCommitObserver observer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(observer);
        registered.TryAdd(identity, observer);
        work?.RegisterCommitObserver(identity, observer);
    }

    public void RegisterSaveParticipant(object identity, IRepositorySaveParticipant<TContext> participant)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(participant);
        work?.RegisterSaveParticipant(identity, participant);
        saveParticipants.TryAdd(identity, participant);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Bound.SaveChangesAsync(cancellationToken);
    public Task<int> SaveChangesAsync(IEnumerable<string> additionalTags, CancellationToken cancellationToken = default) => Bound.SaveChangesAsync(additionalTags, cancellationToken);
    public Task RecordChangesAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default) => Bound.RecordChangesAsync(tags, cancellationToken);
    public ValueTask<IRepositoryTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken cancellationToken = default) => Bound.BeginTransactionAsync(isolationLevel, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        work?.EnsureNotInParticipantSave();
        if (!disposed)
        {
            disposed = true;
            if (work is not null)
            {
                await work.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private RepositoryUnitOfWork<TContext> Bound
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return work ?? throw new InvalidOperationException("Acquire a request-keyed context lease before using this unit of work.");
        }
    }
}