namespace RonSijm.RepoGen.Testing;

/// <summary>An explicitly selected scripted operation was called without a matching arrangement.</summary>
public sealed class UnarrangedRepositoryOperationException : InvalidOperationException
{
    public UnarrangedRepositoryOperationException(string operation)
        : base("No test arrangement matches repository operation '" + operation + "'. Configure its typed script before invoking it.")
    {
        Operation = operation;
    }

    public string Operation { get; }
}
