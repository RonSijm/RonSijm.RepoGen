namespace RonSijm.RepoGen.Design;

public class RepositoryProjectionBuilder
{
    private protected readonly RepositoryProjectionDesign Design;

    internal RepositoryProjectionBuilder(RepositoryProjectionDesign design)
    {
        Design = design;
    }

    public RepositoryProjectionBuilder RequireNavigationVisibilityReview(bool? enabled = true)
    {
        Design.NavigationVisibility.Required = enabled;
        return this;
    }

    public RepositoryProjectionBuilder ReviewNavigationVisibility<TTarget>(string policyName, string reason) where TTarget : class
    {
        Design.NavigationVisibility.Review<TTarget>(policyName, reason);
        return this;
    }

    public RepositoryProjectionBuilder HasMethodName(string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("A method name is required.", nameof(methodName));
        }

        Design.MethodName = methodName.Trim();
        return this;
    }

    /// <summary>Declares annotations once for a named projection and queries using that final shape.</summary>
    public RepositoryProjectionBuilder ResultNullability(Action<RepositoryTypeNullabilityBuilder> configure)
    {
        if (Design.ResultNullability is not null)
        {
            throw new InvalidOperationException($"RGN0005: Projection '{Design.MethodName}' configures result nullability more than once.");
        }

        Design.ResultNullability = RepositoryTypeNullabilityBuilder.Create(Design.ProjectionType, configure);
        return this;
    }
}
