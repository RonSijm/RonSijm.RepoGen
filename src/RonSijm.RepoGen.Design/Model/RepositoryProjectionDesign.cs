using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryProjectionDesign
{
    internal RepositoryNavigationVisibilityConfiguration NavigationVisibility { get; } = new();
    public bool? NavigationVisibilityReviewRequired => NavigationVisibility.Required;
    public IReadOnlyList<RepositoryNavigationVisibilityReview> NavigationVisibilityReviews => NavigationVisibility.Reviews;
    public RepositoryTypeNullabilityDesign? ResultNullability { get; internal set; }
    private readonly RepositoryLoadingConfiguration loading = new();

    internal RepositoryProjectionDesign(
        Type projectionType,
        LambdaExpression expression,
        string? methodName = null,
        bool useExactMethodName = false)
    {
        ProjectionType = projectionType;
        Expression = expression;
        MethodName = methodName;
        UseExactMethodName = useExactMethodName;
    }

    public Type ProjectionType { get; }

    public LambdaExpression Expression { get; internal set; }

    public string? MethodName { get; internal set; }

    public bool UseExactMethodName { get; }

    public IReadOnlyList<RepositoryIncludeDesign> Includes => loading.Includes;
    public IReadOnlyList<string> LoadProfileNames => loading.ProfileNames;
    public bool AutoIncludeNavigations { get; internal set; } = true;

    internal List<RepositoryIncludeDesign> IncludesList => loading.IncludesList;
    internal RepositoryLoadingConfiguration Loading => loading;
}
