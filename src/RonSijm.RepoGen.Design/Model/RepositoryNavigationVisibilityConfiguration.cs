namespace RonSijm.RepoGen.Design;

internal sealed class RepositoryNavigationVisibilityConfiguration
{
    private readonly List<RepositoryNavigationVisibilityReview> reviews = new();
    internal bool? Required { get; set; }
    internal IReadOnlyList<RepositoryNavigationVisibilityReview> Reviews => reviews.AsReadOnly();

    internal void Review<TEntity>(string policyName, string reason) where TEntity : class
    {
        if (string.IsNullOrWhiteSpace(policyName))
        {
            throw new ArgumentException("A visibility policy name is required.", nameof(policyName));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Document why this navigation's visibility is safe or intentionally different.", nameof(reason));
        }

        policyName = policyName.Trim();
        if (reviews.Any(review => review.EntityType == typeof(TEntity) && review.PolicyName == policyName))
        {
            throw new InvalidOperationException($"RGD2003: Navigation visibility for '{typeof(TEntity).FullName}.{policyName}' is reviewed more than once in this configuration scope.");
        }

        reviews.Add(new RepositoryNavigationVisibilityReview(typeof(TEntity), policyName, reason.Trim()));
    }
}
