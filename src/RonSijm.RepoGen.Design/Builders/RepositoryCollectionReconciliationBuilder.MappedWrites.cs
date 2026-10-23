namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCollectionReconciliationBuilder<TChild, TItem> where TChild : class
{
    /// <summary>Deliberately permits one exact typed child scalar on this reconciliation only, not on its parent or other commands.</summary>
    public RepositoryCollectionReconciliationBuilder<TChild, TItem> AllowWrite<TProperty>(string propertyName)
    {
        new RepositoryCommandBuilder<TChild>(design.Writes).AllowWrite<TProperty>(propertyName);
        return this;
    }
}
