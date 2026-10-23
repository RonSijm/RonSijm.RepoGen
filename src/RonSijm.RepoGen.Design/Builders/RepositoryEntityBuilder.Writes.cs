using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity>
{
    /// <summary>Requires explicit command/child-mapping AllowWrite permission for this scalar in closed repositories.</summary>
    /// <remarks>This is a design-time mapping guard, not runtime authorization. False removes this declaration.</remarks>
    public RepositoryEntityBuilder<TEntity> ProtectWrite<TProperty>(Expression<Func<TEntity, TProperty>> property, bool enabled = true)
    {
        string name;
        try
        {
            name = GetProperty(property).Name;
        }
        catch (ArgumentException exception) when (exception is not ArgumentNullException)
        {
            throw new ArgumentException("RGD2902: ProtectWrite requires one direct mapped scalar property.", nameof(property), exception);
        }

        design.ProtectedMappedWritePropertiesDictionary.Remove(name);
        if (enabled)
        {
            design.ProtectedWritePropertiesSet.Add(name);
        }
        else
        {
            design.ProtectedWritePropertiesSet.Remove(name);
        }

        return this;
    }
}
