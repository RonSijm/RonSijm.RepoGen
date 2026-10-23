namespace RonSijm.RepoGen.Design;

/// <summary>Explicit reference annotations for a CLR type. Annotations do not change its CLR identity or query behavior.</summary>
public sealed class RepositoryTypeNullabilityBuilder
{
    private readonly Type type;
    private readonly RepositoryTypeNullabilityBuilder? element;
    private readonly RepositoryTypeNullabilityBuilder[] arguments;
    private bool nullableReference;

    internal RepositoryTypeNullabilityBuilder(Type type)
    {
        this.type = type;
        element = type.IsArray ? new RepositoryTypeNullabilityBuilder(type.GetElementType()!) : null;
        arguments = type.IsGenericType
            ? type.GetGenericArguments().Select(argument => new RepositoryTypeNullabilityBuilder(argument)).ToArray()
            : Array.Empty<RepositoryTypeNullabilityBuilder>();
    }

    /// <summary>Annotates this reference node, not its children. Nullable value types remain defined by T itself.</summary>
    public RepositoryTypeNullabilityBuilder Nullable(bool enabled = true)
    {
        if (enabled && type.IsValueType)
        {
            throw new ArgumentException($"RGN0003: '{type}' is a value type. Use Nullable<T> in the declared CLR type instead of a reference annotation.");
        }

        nullableReference = enabled;
        return this;
    }

    /// <summary>Configures the element of an array, including jagged or rectangular arrays.</summary>
    public RepositoryTypeNullabilityBuilder Element(Action<RepositoryTypeNullabilityBuilder> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        if (element is null)
        {
            throw new ArgumentException($"RGN0003: '{type}' is not an array; it has no element annotation.");
        }

        configure(element);
        return this;
    }

    /// <summary>Configures a zero-based CLR generic argument. Nested types include their declaring type's arguments.</summary>
    public RepositoryTypeNullabilityBuilder TypeArgument(int index, Action<RepositoryTypeNullabilityBuilder> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        if (index < 0 || index >= arguments.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"RGN0003: '{type}' has {arguments.Length} generic arguments.");
        }

        configure(arguments[index]);
        return this;
    }

    internal RepositoryTypeNullabilityDesign Build() => new(type, nullableReference, element?.Build(), arguments.Select(argument => argument.Build()).ToArray());

    internal static RepositoryTypeNullabilityDesign Create(Type type, Action<RepositoryTypeNullabilityBuilder> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var builder = new RepositoryTypeNullabilityBuilder(type);
        configure(builder);
        return builder.Build();
    }
}
