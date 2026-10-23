namespace RonSijm.RepoGen.Design;

/// <summary>Design-time collision policy for a named dictionary terminal. Keys use CLR default equality.</summary>
public enum RepositoryDuplicateKeyBehavior
{
    /// <summary>Reject duplicate keys. This is the compatibility default and does not require ordering.</summary>
    Throw,

    /// <summary>Keep the first value in the declared query order. Requires ordering guaranteed on every branch.</summary>
    KeepFirst,

    /// <summary>Keep the last value in the declared query order. Requires ordering guaranteed on every branch.</summary>
    KeepLast
}
