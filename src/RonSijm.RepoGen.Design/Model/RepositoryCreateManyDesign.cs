using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>A bounded request-to-new-row mapping; creation has no existing-row matching key.</summary>
public sealed class RepositoryCreateManyDesign
{
    internal RepositoryCreateManyDesign(LambdaExpression items, LambdaExpression mapping, int maximumItems)
    {
        Items = items;
        Mapping = mapping;
        MaximumItems = maximumItems;
    }

    public LambdaExpression Items { get; internal set; }
    public LambdaExpression Mapping { get; internal set; }
    public int MaximumItems { get; }
}
