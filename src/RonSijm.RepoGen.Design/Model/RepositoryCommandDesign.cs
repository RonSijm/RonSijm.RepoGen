using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryCommandDesign
{
    internal RepositoryNavigationVisibilityConfiguration NavigationVisibility { get; } = new();
    public bool? NavigationVisibilityReviewRequired => NavigationVisibility.Required;
    public IReadOnlyList<RepositoryNavigationVisibilityReview> NavigationVisibilityReviews => NavigationVisibility.Reviews;
    public IReadOnlyList<RepositoryInputNullabilityDesign> InputNullability { get; internal set; } = Array.Empty<RepositoryInputNullabilityDesign>();
    public RepositoryTypeNullabilityDesign? ResultNullability { get; internal set; }
    public LambdaExpression? ConflictKey { get; internal set; }
    private readonly List<RepositoryInputValidationDesign> validations = new();
    public IReadOnlyList<RepositoryInputValidationDesign> Validations => validations.AsReadOnly();
    internal List<RepositoryInputValidationDesign> ValidationsList => validations;
    public LambdaExpression? ContextKey { get; internal set; }
    private readonly List<string> bypassedVisibility = new();
    public IReadOnlyList<string> BypassedVisibility => bypassedVisibility.AsReadOnly();
    internal List<string> BypassedVisibilityList => bypassedVisibility;
    private readonly List<LambdaExpression> predicates = new();
    private readonly List<RepositoryUpdateDesign> updates = new();
    private readonly List<LambdaExpression> allowedWrites = new();
    private readonly List<RepositoryUpdateDesign> expectedVersions = new();
    private readonly List<RepositoryCollectionReconciliationDesign> collections = new();

    public IReadOnlyList<RepositoryCollectionReconciliationDesign> Collections => collections.AsReadOnly();
    internal List<RepositoryCollectionReconciliationDesign> CollectionsList => collections;

    internal RepositoryCommandDesign(string methodName)
    {
        MethodName = methodName;
    }

    public string MethodName { get; }

    public RepositoryCommandTimeoutDesign? CommandTimeout { get; internal set; }

    public RepositoryCommandKind Kind { get; internal set; }
    public LambdaExpression? CreateMapping { get; internal set; }
    public LambdaExpression? ResultProjection { get; internal set; }
    public bool ResultFromSaved { get; internal set; }
    public bool ReturnsMany { get; internal set; }
    public RepositoryBatchCommandDesign? Batch { get; internal set; }
    public RepositoryCreateManyDesign? CreateMany { get; internal set; }
    public bool? SaveChanges { get; internal set; }
    public bool ReloadAfterSave { get; internal set; }
    public bool WithOutcome { get; internal set; }
    public string? TestOverrideReason { get; internal set; }
    public bool InMemoryImmediateWrites { get; internal set; }
    public bool InMemoryCollectionWrites { get; internal set; }
    public bool InMemoryTranslations { get; internal set; }
    public bool InMemoryModelQueryFilterEnabled { get; internal set; }
    /// <summary>Separate permission for filtered navigation reads in this command's fresh saved DTO.</summary>
    public bool InMemoryModelQueryFilterNavigationsEnabled { get; internal set; }
    public IReadOnlyList<LambdaExpression> AllowedWrites => allowedWrites.AsReadOnly();
    public IReadOnlyList<RepositoryUpdateDesign> ExpectedVersions => expectedVersions.AsReadOnly();
    internal List<LambdaExpression> AllowedWritesList => allowedWrites;
    internal List<RepositoryUpdateDesign> ExpectedVersionsList => expectedVersions;

    public IReadOnlyList<LambdaExpression> Predicates => new ReadOnlyCollection<LambdaExpression>(predicates);

    public IReadOnlyList<RepositoryUpdateDesign> Updates => new ReadOnlyCollection<RepositoryUpdateDesign>(updates);

    internal List<LambdaExpression> PredicatesList => predicates;

    internal List<RepositoryUpdateDesign> UpdatesList => updates;
}
