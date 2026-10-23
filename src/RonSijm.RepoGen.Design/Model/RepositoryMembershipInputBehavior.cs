namespace RonSijm.RepoGen.Design;

/// <summary>EmptySet preserves set semantics: IN an empty set matches nothing; NOT IN matches everything.</summary>
public enum RepositoryMembershipInputBehavior
{
    EmptySet,
    IgnoreFilter,
    Reject
}
