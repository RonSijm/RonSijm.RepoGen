namespace RonSijm.RepoGen;

/// <summary>
/// Declares that the annotated type can be projected from <typeparamref name="TEntity"/>.
/// </summary>
/// <typeparam name="TEntity">The selector-enabled entity to project.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProjectionFromAttribute<TEntity> : Attribute
    where TEntity : class
{
    /// <summary>
    /// Gets or sets the generated single-selector method base name. The generator adds
    /// <c>As</c> and <c>Async</c> when they are omitted.
    /// </summary>
    public string? MethodName { get; set; }
}