using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryAccessorDesign
{
    internal RepositoryAccessorDesign(string propertyName, Type propertyType)
    {
        PropertyName = propertyName;
        PropertyType = propertyType;
    }

    public string PropertyName { get; }
    public Type PropertyType { get; }
    public RepositoryTypeAdapterDesign? InputAdapter { get; internal set; }
    public LambdaExpression? InputMapping { get; internal set; }
    public Type? ValueConverterInputType { get; internal set; }
    public RepositoryTypeNullabilityDesign? InputNullability { get; internal set; }
    public bool UniquenessConfigured { get; internal set; }
    public bool UseInferredUniqueness { get; internal set; }

    public bool IsUnique { get; internal set; }

    public string? MethodName { get; internal set; }
}
