using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryCollectionReconciliationDesign
{
    internal RepositoryCollectionReconciliationDesign(LambdaExpression navigation, LambdaExpression items,
        LambdaExpression childKey, LambdaExpression itemKey)
    {
        Navigation = navigation;
        Items = items;
        ChildKey = childKey;
        ItemKey = itemKey;
    }

    public LambdaExpression Navigation { get; }
    public LambdaExpression Items { get; }
    public LambdaExpression ChildKey { get; }
    public LambdaExpression ItemKey { get; }
    public LambdaExpression? CreateMapping { get; internal set; }
    public LambdaExpression? Condition { get; internal set; }
    public int MaximumItems { get; internal set; } = 1000;
    public RepositoryCollectionRemovalBehavior MissingItems { get; internal set; }
    public bool IsLinkReconciliation { get; internal set; }
    public bool SharedReferenceColumnsEnabled { get; internal set; }
    public LambdaExpression? TargetReference { get; internal set; }
    internal List<RepositoryLinkReferenceDesign> AdditionalReferencesList { get; } = [];
    public IReadOnlyList<RepositoryLinkReferenceDesign> AdditionalReferences => AdditionalReferencesList;

    internal RepositoryCommandDesign Writes { get; } = new("collection") { Kind = RepositoryCommandKind.Patch };
    public IReadOnlyList<RepositoryUpdateDesign> Updates => Writes.Updates;
    public IReadOnlyList<RepositoryExpectedVersionDesign> ExpectedVersions => Writes.ExpectedVersions;
    public IReadOnlyList<LambdaExpression> AllowedWrites => Writes.AllowedWrites;
    public IReadOnlyDictionary<string, Type> AllowedMappedWrites => Writes.AllowedMappedWrites;
}
