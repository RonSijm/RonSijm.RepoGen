using System.Data;

namespace RonSijm.RepoGen.Abstractions;

/// <summary>Optional commit-coordination capability used by infrastructure such as caching.</summary>
public interface IRepositoryCommitSource
{
    void RegisterCommitObserver(object identity, IRepositoryCommitObserver observer);
}

/// <summary>Saves all changes in one repository context, not only one entity type.</summary>
public interface IRepositoryUnitOfWork : IAsyncDisposable
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves tracked changes and records additional dependency tags at the same commit boundary.</summary>
    Task<int> SaveChangesAsync(IEnumerable<string> additionalTags, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This unit of work does not support commit-coordinated additional dependency tags.");

    ValueTask<IRepositoryTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        CancellationToken cancellationToken = default);

    /// <summary>Records immediately executed writes for commit observers.</summary>
    Task RecordChangesAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);
}

/// <summary>An explicit transaction; committing does not implicitly save staged changes.</summary>
public interface IRepositoryTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>Observes database commits, for example to invalidate affected cache tags.</summary>
public interface IRepositoryCommitObserver
{
    Task CommittedAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default);
}

/// <summary>The database committed, but a subsequent commit observer failed.</summary>
public sealed class RepositoryCommitObserverException : RepositoryCommitException
{
    public RepositoryCommitObserverException(Exception innerException)
        : base("The database committed, but a repository commit observer failed. Do not retry the database write blindly.", innerException) { }
}

/// <summary>A failure after a successful database commit; retrying the write is unsafe.</summary>
public abstract class RepositoryCommitException : Exception
{
    protected RepositoryCommitException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>The database committed, but releasing transaction resources failed.</summary>
public sealed class RepositoryCommitCleanupException : RepositoryCommitException
{
    public RepositoryCommitCleanupException(Exception innerException)
        : base("The database committed, but transaction cleanup failed. Do not retry the database write blindly.", innerException) { }
}