namespace RonSijm.RepoGen.Design;

/// <summary>An explicit SQL-command timeout override. Null seconds retain the context/provider setting rather than an inherited designer timeout.</summary>
public sealed class RepositoryCommandTimeoutDesign
{
    internal RepositoryCommandTimeoutDesign(int? seconds)
    {
        if (seconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "RGD2301: An explicit command timeout must be positive, or null to use the context setting.");
        }

        Seconds = seconds;
    }

    public int? Seconds { get; }
}
