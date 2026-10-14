namespace RonSijm.RepoGen.Abstractions;

public enum CommandStatus
{
    Applied,
    Staged,
    NotFound,
    Conflict,
    NotMatched,
    NoChanges,
    Inserted,
    Updated,
    AlreadyExists
}

/// <summary>An explicit write outcome. A staged operation has not persisted anything.</summary>
public class CommandResult
{
    public CommandResult(CommandStatus status, int affectedRows = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(affectedRows);
        Status = status;
        AffectedRows = affectedRows;
    }

    public CommandStatus Status { get; }

    /// <summary>The shared save count for tracked writes, or matched rows for set-based writes.</summary>
    public int AffectedRows { get; }

    public bool Succeeded => Status is CommandStatus.Applied or CommandStatus.Inserted or CommandStatus.Updated;
}

/// <summary>Contains a deliberately configured result only after a successful saved operation.</summary>
public sealed class CommandResult<T> : CommandResult
{
    public CommandResult(CommandStatus status, int affectedRows = 0, T? value = default, bool hasValue = false)
        : base(status, affectedRows)
    {
        Value = value;
        HasValue = hasValue;
    }

    public T? Value { get; }
    public bool HasValue { get; }
    public CommandResult WithoutValue() => new(Status, AffectedRows);
}

/// <summary>A concurrency failure elsewhere in a shared save; it is not attributed to this command.</summary>
public sealed class RepositoryWriteConflictException : Exception
{
    public RepositoryWriteConflictException(Exception innerException)
        : base("A different tracked operation conflicted during the shared save. Resolve the unit of work explicitly; this command was not retried.", innerException) { }
}

/// <summary>The save succeeded, but producing its configured result failed. Do not repeat the write blindly.</summary>
public sealed class RepositorySavedResultException : Exception
{
    public RepositorySavedResultException(Exception innerException)
        : base("The write was saved, but its result could not be produced. An active transaction may still be rolled back; do not repeat the write blindly.", innerException) { }
}