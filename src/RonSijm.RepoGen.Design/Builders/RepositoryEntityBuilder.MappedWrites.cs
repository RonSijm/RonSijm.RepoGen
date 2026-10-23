namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity> where TEntity : class
{
    /// <summary>Requires explicit command permission for an exact named mapped scalar in closed repositories.</summary>
    /// <remarks>Design-time assignment protection, not runtime authorization. False removes the declaration.</remarks>
    public RepositoryEntityBuilder<TEntity> ProtectWrite<TProperty>(string propertyName, bool enabled = true)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            throw new ArgumentException("RGD1311: Mapped write protection needs a property name.", nameof(propertyName));
        if (enabled)
        {
            design.ProtectedWritePropertiesSet.Add(propertyName);
            design.ProtectedMappedWritePropertiesDictionary[propertyName] = typeof(TProperty);
        }
        else
        {
            design.ProtectedWritePropertiesSet.Remove(propertyName);
            design.ProtectedMappedWritePropertiesDictionary.Remove(propertyName);
        }
        return this;
    }
}
