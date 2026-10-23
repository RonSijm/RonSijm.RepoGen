using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>A caller's original token, not an assignment to the current entity.</summary>
public sealed class RepositoryExpectedVersionDesign
{
    internal RepositoryExpectedVersionDesign(LambdaExpression property, LambdaExpression value)
    {
        Property = property;
        PropertyType = property.ReturnType;
        Value = value;
    }

    internal RepositoryExpectedVersionDesign(string propertyName, Type propertyType, LambdaExpression value)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            throw new ArgumentException("RGD1310: A mapped concurrency-token property name is required.", nameof(propertyName));
        MappedPropertyName = propertyName;
        PropertyType = propertyType;
        Value = value;
    }

    public LambdaExpression? Property { get; internal set; }
    public string? MappedPropertyName { get; }
    public Type PropertyType { get; }
    public LambdaExpression Value { get; internal set; }
}
