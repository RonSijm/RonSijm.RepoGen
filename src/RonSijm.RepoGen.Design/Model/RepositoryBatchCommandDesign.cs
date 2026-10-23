using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>A bounded root-row command with one fixed input item type and an explicit unique matching key.</summary>
public sealed class RepositoryBatchCommandDesign
{
    internal RepositoryBatchCommandDesign(LambdaExpression items, LambdaExpression entityKey, LambdaExpression itemKey, int maximumItems)
    {
        Items = items;
        EntityKey = entityKey;
        ItemKey = itemKey;
        MaximumItems = maximumItems;
    }

    public LambdaExpression Items { get; internal set; }
    public LambdaExpression EntityKey { get; internal set; }
    public LambdaExpression ItemKey { get; internal set; }
    public int MaximumItems { get; }
    internal RepositoryCommandDesign Mapping { get; } = new("batch item");
    public IReadOnlyList<RepositoryUpdateDesign> Updates => Mapping.Updates;
    public IReadOnlyList<RepositoryExpectedVersionDesign> ExpectedVersions => Mapping.ExpectedVersions;
}
