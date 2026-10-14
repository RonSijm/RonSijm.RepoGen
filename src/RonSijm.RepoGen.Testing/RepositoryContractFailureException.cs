namespace RonSijm.RepoGen.Testing;

/// <summary>Identifies the failing case; assertion and cleanup failures are both retained.</summary>
public sealed class RepositoryContractFailureException : Exception
{
    public RepositoryContractFailureException(string caseName, RepositoryContractFailurePhase phase, Exception innerException)
        : base("Repository contract case '" + caseName + "' failed during " + phase + ".", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseName);
        ArgumentNullException.ThrowIfNull(innerException);
        CaseName = caseName;
        Phase = phase;
    }

    public string CaseName { get; }
    public RepositoryContractFailurePhase Phase { get; }
}
