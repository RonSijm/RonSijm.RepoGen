using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryUpdateDesign
{
    internal RepositoryUpdateDesign(LambdaExpression property, LambdaExpression value, LambdaExpression? condition = null)
    {
        Property = property;
        PropertyType = property.ReturnType;
        Value = value;
        Condition = condition;
    }

    internal RepositoryUpdateDesign(string propertyName, Type propertyType, LambdaExpression value, LambdaExpression? condition = null)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            throw new ArgumentException("RGD1311: A mapped setter property name is required.", nameof(propertyName));
        MappedPropertyName = propertyName;
        PropertyType = propertyType;
        Value = value;
        Condition = condition;
    }

    public LambdaExpression? Property { get; internal set; }
    public string? MappedPropertyName { get; }
    public Type PropertyType { get; }

    public LambdaExpression Value { get; internal set; }
    public LambdaExpression? Condition { get; internal set; }
}
