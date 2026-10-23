namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCommandBuilder<TEntity> where TEntity : class
{
    /// <summary>Explicitly permits an exact named protected scalar, such as a caller-advanced shadow token.</summary>
    /// <remarks>Does not permit keys, relationship identity, generated columns or unsupported command lifecycles.</remarks>
    public RepositoryCommandBuilder<TEntity> AllowWrite<TProperty>(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            throw new ArgumentException("RGD1311: A mapped write permission needs a property name.", nameof(propertyName));
        design.AllowedMappedWritesDictionary[propertyName] = typeof(TProperty);
        return this;
    }
}
