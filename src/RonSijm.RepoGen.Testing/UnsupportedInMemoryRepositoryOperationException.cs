namespace RonSijm.RepoGen.Testing;

/// <summary>A designer operation cannot run with portable fixture semantics and has no matching test override.</summary>
public sealed class UnsupportedInMemoryRepositoryOperationException : InvalidOperationException
{
    public UnsupportedInMemoryRepositoryOperationException(string operation, string reason)
        : base("In-memory repository operation requires an explicit test override: " + operation + ". " + reason)
    {
        Operation = operation;
        Reason = reason;
    }

    public string Operation { get; }
    public string Reason { get; }
}
