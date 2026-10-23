using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryQueryDesign
{
    public RepositoryQueryTarget Target { get; internal set; }
    public string? TestOverrideReason { get; internal set; }
    internal RepositoryNavigationVisibilityConfiguration NavigationVisibility { get; } = new();
    public bool? NavigationVisibilityReviewRequired => NavigationVisibility.Required;
    public IReadOnlyList<RepositoryNavigationVisibilityReview> NavigationVisibilityReviews => NavigationVisibility.Reviews;
    public IReadOnlyList<RepositoryInputNullabilityDesign> InputNullability { get; internal set; } = Array.Empty<RepositoryInputNullabilityDesign>();
    public RepositoryTypeNullabilityDesign? ResultNullability { get; internal set; }
    public RepositoryTypeNullabilityDesign? DictionaryKeyNullability { get; internal set; }
    public RepositoryPageConsistency PageConsistency { get; internal set; }
    public bool RequirePagingOrder { get; internal set; }
    public RepositoryRegisteredQueryDesign? RegisteredSource { get; internal set; }
    private readonly List<RepositoryInputValidationDesign> validations = new();
    public IReadOnlyList<RepositoryInputValidationDesign> Validations => validations.AsReadOnly();
    internal List<RepositoryInputValidationDesign> ValidationsList => validations;
    private readonly List<RepositoryCursorValidationDesign> cursorValidations = new();
    public IReadOnlyList<RepositoryCursorValidationDesign> CursorValidations => cursorValidations.AsReadOnly();
    internal List<RepositoryCursorValidationDesign> CursorValidationsList => cursorValidations;
    public LambdaExpression? ContextKey { get; internal set; }
    private readonly List<string> bypassedVisibility = new();
    public IReadOnlyList<string> BypassedVisibility => bypassedVisibility.AsReadOnly();
    internal List<string> BypassedVisibilityList => bypassedVisibility;
    private readonly List<LambdaExpression> predicates = new();
    private readonly RepositoryLoadingConfiguration loading = new();
    private readonly List<RepositorySortDesign> sorts = new();
    private readonly List<RepositoryQueryStageDesign> resultStages = new();
    private readonly List<RepositoryConditionalPredicateDesign> conditionalPredicates = new();
    private readonly List<RepositoryMembershipDesign> memberships = new();

    internal RepositoryQueryDesign(string methodName)
    {
        MethodName = methodName;
        Cache = new RepositoryQueryCacheDesign();
    }

    public string MethodName { get; }

    public RepositoryCommandTimeoutDesign? CommandTimeout { get; internal set; }

    public IReadOnlyList<LambdaExpression> Predicates => new ReadOnlyCollection<LambdaExpression>(predicates);

    public IReadOnlyList<RepositoryIncludeDesign> Includes => loading.Includes;
    public IReadOnlyList<string> LoadProfileNames => loading.ProfileNames;

    public IReadOnlyList<RepositorySortDesign> Sorts => new ReadOnlyCollection<RepositorySortDesign>(sorts);

    public IReadOnlyList<RepositoryConditionalPredicateDesign> ConditionalPredicates => new ReadOnlyCollection<RepositoryConditionalPredicateDesign>(conditionalPredicates);
    public IReadOnlyList<RepositoryMembershipDesign> Memberships => memberships.AsReadOnly();

    public RepositoryProjectionDesign? Projection { get; internal set; }

    public RepositoryQuerySourceDesign? Source { get; internal set; }

    internal Type? ProjectionTypeReference { get; set; }

    internal string? ProjectionMethodNameReference { get; set; }

    public RepositoryDynamicSortDesign? DynamicSort { get; internal set; }

    public RepositoryCursorDesign? Cursor { get; internal set; }

    public RepositoryGroupedQueryDesign? Group { get; internal set; }

    public Type? ResultInputType { get; internal set; }

    public IReadOnlyList<RepositoryQueryStageDesign> ResultStages => new ReadOnlyCollection<RepositoryQueryStageDesign>(resultStages);

    public RepositoryDictionaryDesign? Dictionary { get; internal set; }

    public RepositoryScalarDesign? Scalar { get; internal set; }

    public RepositoryQueryResultKind ResultKind { get; internal set; } = RepositoryQueryResultKind.List;

    public RepositoryTrackingBehavior TrackingBehavior { get; internal set; }

    public RepositoryQuerySplittingBehavior SplittingBehavior { get; internal set; }

    public bool IgnoreQueryFilters { get; internal set; }

    public bool IgnoreAutoIncludes { get; internal set; }

    public string? QueryTag { get; internal set; }

    public RepositoryQueryCacheDesign Cache { get; }

    public bool UseDefaultSort { get; internal set; } = true;
    public bool RequireExplicitLoadStrategy { get; internal set; }

    internal List<LambdaExpression> PredicatesList => predicates;

    internal List<RepositoryIncludeDesign> IncludesList => loading.IncludesList;
    internal RepositoryLoadingConfiguration Loading => loading;

    internal List<RepositorySortDesign> SortsList => sorts;

    internal List<RepositoryQueryStageDesign> ResultStagesList => resultStages;

    internal List<RepositoryConditionalPredicateDesign> ConditionalPredicatesList => conditionalPredicates;
    internal List<RepositoryMembershipDesign> MembershipsList => memberships;
}
