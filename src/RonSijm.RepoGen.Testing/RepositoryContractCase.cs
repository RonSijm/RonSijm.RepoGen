namespace RonSijm.RepoGen.Testing;

/// <summary>A provider-independent assertion over the application repository port.</summary>
public sealed class RepositoryContractCase<TRepository> where TRepository : class
{
    public RepositoryContractCase(string name, Func<TRepository, CancellationToken, Task> assertion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(assertion);
        Name = name;
        Assertion = assertion;
    }

    public string Name { get; }
    internal Func<TRepository, CancellationToken, Task> Assertion { get; }
}
