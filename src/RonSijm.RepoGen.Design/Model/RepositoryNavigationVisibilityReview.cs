namespace RonSijm.RepoGen.Design;

/// <summary>A design-time review receipt, not a filter or authorization rule.</summary>
public sealed class RepositoryNavigationVisibilityReview
{
    internal RepositoryNavigationVisibilityReview(Type entityType, string policyName, string reason)
    {
        EntityType = entityType;
        PolicyName = policyName;
        Reason = reason;
    }

    public Type EntityType { get; }
    public string PolicyName { get; }
    public string Reason { get; }
}
