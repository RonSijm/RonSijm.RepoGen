using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCollectionReconciliationBuilder<TChild, TItem> where TChild : class
{
    /// <summary>Checks a matched child's exact typed mapped token from its input item, including shadow tokens.</summary>
    public RepositoryCollectionReconciliationBuilder<TChild, TItem> ExpectVersion<TProperty>(string propertyName, Expression<Func<TItem, TProperty>> expected)
    {
        new RepositoryCommandBuilder<TChild>(design.Writes).ExpectVersion(propertyName, expected);
        return this;
    }
}
