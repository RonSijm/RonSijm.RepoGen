using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>Expression trees erase reference annotations; this deliberately declares an input's annotation tree.</summary>
public sealed class RepositoryInputNullabilityDesign
{
    internal RepositoryInputNullabilityDesign(string parameterName, Type inputType)
        : this(parameterName, RepositoryTypeNullabilityBuilder.Create(inputType, type => type.Nullable())) { }

    internal RepositoryInputNullabilityDesign(string parameterName, RepositoryTypeNullabilityDesign nullability)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            throw new ArgumentException("An input parameter name is required.", nameof(parameterName));
        }

        ParameterName = parameterName.Trim();
        Nullability = nullability;
    }

    public string ParameterName { get; }
    public Type InputType => Nullability.Type;
    public RepositoryTypeNullabilityDesign Nullability { get; }
}
