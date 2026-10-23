namespace RonSijm.RepoGen.Design;

/// <summary>The CQRS capability that exposes a named query. Does not authorize access or acquire a lock.</summary>
public enum RepositoryQueryTarget
{
    /// <summary>The default read-model surface and its independent read tracking policy.</summary>
    Read,

    /// <summary>A command-side read, exposed only on the write surface and using the ordinary tracking policy.</summary>
    Write
}
