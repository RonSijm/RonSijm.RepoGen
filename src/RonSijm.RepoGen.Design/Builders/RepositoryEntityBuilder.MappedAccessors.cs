namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity> where TEntity : class
{
    /// <summary>Explicitly binds an accessor to an exact typed EF scalar property, including a shadow property.</summary>
    /// <remarks>Does not infer accessors for other shadow keys/indexes. Use HasMethodName for a model
    /// property name that is not a C# identifier. No EF property name is supplied by service callers.</remarks>
    public RepositoryAccessorBuilder HasAccessor<TProperty>(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            throw new ArgumentException("RGD1710: A mapped accessor property name is required.", nameof(propertyName));
        var accessor = design.AccessorsList.FirstOrDefault(item => item.PropertyName == propertyName);
        if (accessor is null)
        {
            accessor = new RepositoryAccessorDesign(propertyName, typeof(TProperty));
            design.AccessorsList.Add(accessor);
        }
        else if (accessor.PropertyType != typeof(TProperty))
            throw new InvalidOperationException("RGD1710: A mapped accessor cannot be rebound with a different CLR type.");
        accessor.UsesMappedPropertyName = true;
        accessor.UseInferredUniqueness = true;
        return new RepositoryAccessorBuilder(accessor);
    }
}
