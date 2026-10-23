using System.Globalization;
using System.Reflection;

namespace RonSijm.RepoGen.Tool;

public enum RepositoryApiDataKind
{
    Class,
    Struct,
    Interface,
    Enum
}

public enum RepositoryApiDataMemberKind
{
    Property,
    Field
}

public sealed record RepositoryApiDataMember(
    string Name,
    RepositoryApiDataMemberKind Kind,
    string Type,
    bool Readable,
    bool Writable,
    bool InitOnly,
    bool Required,
    string? WriteType = null);

public sealed record RepositoryApiDataConstructor(string Signature, IReadOnlyList<RepositoryApiParameter> Parameters, bool SetsRequiredMembers = false);

public sealed record RepositoryApiEnumValue(string Name, string Value);

public sealed record RepositoryApiGenericParameter(
    string Name,
    string Attributes,
    IReadOnlyList<string> Constraints,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    byte? NullableConstraint = null);

/// <summary>Public data members and construction metadata, not serialization rules or a complete type API.</summary>
/// <remarks>Generic definitions use CLR identity (including arity); member types retain generic parameter names and nullable annotations.</remarks>
public sealed record RepositoryApiDataContract(
    string Type,
    RepositoryApiDataKind Kind,
    bool Abstract,
    bool Sealed,
    IReadOnlyList<string> Bases,
    IReadOnlyList<RepositoryApiDataMember> Members,
    IReadOnlyList<RepositoryApiDataConstructor> Constructors,
    IReadOnlyList<RepositoryApiEnumValue> EnumValues,
    IReadOnlyList<RepositoryApiGenericParameter> GenericParameters,
    string? EnumUnderlyingType = null,
    bool Flags = false);

internal static class RepositoryApiDataContractCapture
{
    private const BindingFlags PublicInstanceDeclared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    private const string RequiredAttribute = "System.Runtime.CompilerServices.RequiredMemberAttribute";
    private const string SetsRequiredAttribute = "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute";

    public static IReadOnlyList<RepositoryApiDataContract> Capture(IEnumerable<Type> roots)
    {
        var seen = new HashSet<Type>();
        var identities = new Dictionary<string, Type>(StringComparer.Ordinal);
        var result = new List<RepositoryApiDataContract>();
        var nullability = new NullabilityInfoContext();
        foreach (var root in roots)
        {
            Visit(root);
        }

        return Freeze(result);

        void Visit(Type type)
        {
            if (type is null || type.IsByRef || type.IsPointer || type.IsFunctionPointer)
            {
                throw Invalid($"Unsupported data-contract type '{type}'. Pointer/ref shapes are not data contracts.");
            }

            if (type.IsGenericParameter)
            {
                return;
            }

            if (type.IsArray)
            {
                Visit(type.GetElementType()!);
                return;
            }

            // Visit closed arguments even when the container is a framework/RepoGen type.
            // Expand each application generic definition only once: Node<List<T>> can otherwise recurse forever.
            if (type.IsConstructedGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    Visit(argument);
                }
            }

            if (FrameworkType(type))
            {
                return;
            }

            if (type.IsGenericType)
            {
                type = type.GetGenericTypeDefinition();
            }

            if (!seen.Add(type))
            {
                return;
            }

            if (!type.IsVisible || typeof(Delegate).IsAssignableFrom(type))
            {
                throw Invalid($"Data-contract type '{type}' must be public and not a delegate.");
            }

            var identity = "global::" + type.FullName!.Replace('+', '.');
            if (identities.TryGetValue(identity, out var prior) && prior != type)
            {
                throw Invalid($"Data-contract identity '{identity}' is defined by multiple assemblies.");
            }

            identities.Add(identity, type);
            var kind = type.IsEnum ? RepositoryApiDataKind.Enum :
                type.IsInterface ? RepositoryApiDataKind.Interface :
                type.IsValueType ? RepositoryApiDataKind.Struct : RepositoryApiDataKind.Class;
            var bases = new List<string>();
            if (type.BaseType is { } parent && !FrameworkType(parent))
            {
                bases.Add(RepositoryTypeName.Clr(parent));
                Visit(parent);
            }

            foreach (var parentInterface in type.GetInterfaces())
            {
                Visit(parentInterface);
                bases.Add(RepositoryTypeName.Clr(parentInterface));
            }

            var members = new List<RepositoryApiDataMember>();
            if (!type.IsEnum)
            {
                foreach (var property in type.GetProperties(PublicInstanceDeclared).Where(static property => property.GetIndexParameters().Length == 0))
                {
                    var read = property.GetMethod?.IsPublic == true;
                    var write = property.SetMethod?.IsPublic == true;
                    if (!read && !write)
                    {
                        continue;
                    }

                    Visit(property.PropertyType);
                    var info = nullability.Create(property);
                    var init = write && property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()
                        .Any(static modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");
                    members.Add(new(property.Name, RepositoryApiDataMemberKind.Property, RepositoryTypeName.Annotated(info),
                        read, write, init, HasAttribute(property, RequiredAttribute), write ? RepositoryTypeName.Annotated(info, write: true) : null));
                }

                foreach (var field in type.GetFields(PublicInstanceDeclared))
                {
                    Visit(field.FieldType);
                    var info = nullability.Create(field);
                    members.Add(new(field.Name, RepositoryApiDataMemberKind.Field, RepositoryTypeName.Annotated(info),
                        true, !field.IsInitOnly, false, HasAttribute(field, RequiredAttribute), field.IsInitOnly ? null : RepositoryTypeName.Annotated(info, write: true)));
                }
            }

            var constructors = type.GetConstructors(PublicInstanceDeclared).Select(constructor =>
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    Visit(parameter.ParameterType);
                }

                return new RepositoryApiDataConstructor("(" + string.Join(",", constructor.GetParameters().Select(static parameter => RepositoryTypeName.Clr(parameter.ParameterType))) + ")",
                    constructor.GetParameters().Select(parameter => new RepositoryApiParameter(parameter.Name!, RepositoryTypeName.Annotated(nullability.Create(parameter)),
                        parameter.IsOptional, parameter.IsOptional ? RepositoryApiSnapshot.DefaultValue(parameter) : null)).ToArray(), HasAttribute(constructor, SetsRequiredAttribute));
            }).ToList();
            // Reflection omits the implicit struct constructor, but source callers can still use new S().
            if (kind == RepositoryApiDataKind.Struct && constructors.All(static constructor => constructor.Parameters.Count != 0))
            {
                constructors.Add(new("()", []));
            }

            var generic = type.IsGenericTypeDefinition
                ? type.GetGenericArguments().Select(parameter =>
                {
                    foreach (var constraint in parameter.GetGenericParameterConstraints())
                    {
                        Visit(constraint);
                    }

                    return new RepositoryApiGenericParameter(parameter.Name, parameter.GenericParameterAttributes.ToString(),
                        parameter.GetGenericParameterConstraints().Select(RepositoryTypeName.Clr).ToArray(), NullableConstraint(parameter));
                }).ToArray()
                : [];
            var values = type.IsEnum
                ? type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(static field =>
                    new RepositoryApiEnumValue(field.Name, Convert.ToString(field.GetRawConstantValue(), CultureInfo.InvariantCulture)!)).ToArray()
                : [];
            result.Add(new(identity, kind, type.IsAbstract, type.IsSealed, bases, members, constructors, values, generic,
                type.IsEnum ? RepositoryTypeName.Clr(Enum.GetUnderlyingType(type)) : null, type.IsEnum && HasAttribute(type, typeof(FlagsAttribute).FullName!)));
        }
    }

    private static bool HasAttribute(MemberInfo member, string name) => member.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == name);

    private static byte? NullableConstraint(Type parameter)
    {
        var annotation = parameter.CustomAttributes.FirstOrDefault(static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.NullableAttribute");
        if (annotation?.ConstructorArguments.SingleOrDefault().Value is byte direct)
        {
            return direct;
        }

        if (annotation?.ConstructorArguments.SingleOrDefault().Value is IEnumerable<CustomAttributeTypedArgument> arguments && arguments.FirstOrDefault().Value is byte first)
        {
            return first;
        }

        for (var scope = parameter.DeclaringType; scope is not null; scope = scope.DeclaringType)
        {
            var context = scope.CustomAttributes.FirstOrDefault(static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.NullableContextAttribute");
            if (context?.ConstructorArguments.SingleOrDefault().Value is byte value)
            {
                return value;
            }
        }

        return null;
    }

    private static bool FrameworkType(Type type)
    {
        var assembly = type.Assembly.GetName().Name!;
        return assembly is "mscorlib" or "netstandard" or "System" or "Microsoft.CSharp" or "WindowsBase" ||
               assembly.StartsWith("System.", StringComparison.Ordinal) || assembly.StartsWith("Microsoft.", StringComparison.Ordinal) ||
               assembly is "RonSijm.RepoGen.Abstractions" or "RonSijm.RepoGen.Design" or "RonSijm.RepoGen.EntityFrameworkCore" or
                   "RonSijm.RepoGen.Caching" or "RonSijm.RepoGen.SqlServer" or "RonSijm.RepoGen.Sqlite" or "RonSijm.RepoGen.Tool";
    }

    internal static IReadOnlyList<RepositoryApiDataContract> Freeze(IEnumerable<RepositoryApiDataContract> contracts)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return Array.AsReadOnly(contracts.Select(contract =>
        {
            if (contract is null || string.IsNullOrWhiteSpace(contract.Type) || !names.Add(contract.Type) || !Enum.IsDefined(contract.Kind) ||
                contract.Bases is null || contract.Members is null || contract.Constructors is null || contract.EnumValues is null || contract.GenericParameters is null ||
                contract.Bases.Any(string.IsNullOrWhiteSpace) || contract.Bases.Distinct(StringComparer.Ordinal).Count() != contract.Bases.Count)
            {
                throw Invalid("Invalid or duplicate data contract.");
            }

            var memberNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in contract.Members)
            {
                if (member is null || string.IsNullOrWhiteSpace(member.Name) || !memberNames.Add(member.Name) || !Enum.IsDefined(member.Kind) ||
                    string.IsNullOrWhiteSpace(member.Type) || !member.Readable && !member.Writable ||
                    member.InitOnly && (!member.Writable || member.Kind != RepositoryApiDataMemberKind.Property) ||
                    member.Writable != !string.IsNullOrWhiteSpace(member.WriteType))
                {
                    throw Invalid("Invalid or duplicate data member.");
                }
            }

            var constructorNames = new HashSet<string>(StringComparer.Ordinal);
            var constructors = contract.Constructors.Select(constructor =>
            {
                if (constructor is null || string.IsNullOrWhiteSpace(constructor.Signature) || !constructorNames.Add(constructor.Signature) || constructor.Parameters is null ||
                    constructor.Parameters.Any(static parameter => parameter is null || string.IsNullOrWhiteSpace(parameter.Name) || string.IsNullOrWhiteSpace(parameter.Type)) ||
                    constructor.Parameters.Select(static parameter => parameter.Name).Distinct(StringComparer.Ordinal).Count() != constructor.Parameters.Count)
                {
                    throw Invalid("Invalid or duplicate data constructor.");
                }

                return constructor with { Parameters = Array.AsReadOnly(constructor.Parameters.ToArray()) };
            }).OrderBy(static constructor => constructor.Signature, StringComparer.Ordinal).ToArray();
            var enumNames = new HashSet<string>(StringComparer.Ordinal);
            if (contract.EnumValues.Any(value => value is null || string.IsNullOrWhiteSpace(value.Name) || !enumNames.Add(value.Name) || string.IsNullOrWhiteSpace(value.Value)) ||
                (contract.Kind == RepositoryApiDataKind.Enum) != (contract.EnumUnderlyingType is not null) ||
                contract.Kind != RepositoryApiDataKind.Enum && (contract.EnumValues.Count > 0 || contract.Flags))
            {
                throw Invalid("Invalid enum metadata.");
            }

            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            var generic = contract.GenericParameters.Select(parameter =>
            {
                if (parameter is null || string.IsNullOrWhiteSpace(parameter.Name) || !parameterNames.Add(parameter.Name) || string.IsNullOrWhiteSpace(parameter.Attributes) ||
                    parameter.NullableConstraint is > 2 || parameter.Constraints is null || parameter.Constraints.Any(string.IsNullOrWhiteSpace) ||
                    parameter.Constraints.Distinct(StringComparer.Ordinal).Count() != parameter.Constraints.Count)
                {
                    throw Invalid("Invalid generic parameter metadata.");
                }

                return parameter with { Constraints = Array.AsReadOnly(parameter.Constraints.Order(StringComparer.Ordinal).ToArray()) };
            }).ToArray();
            return contract with
            {
                Bases = Array.AsReadOnly(contract.Bases.Order(StringComparer.Ordinal).ToArray()),
                Members = Array.AsReadOnly(contract.Members.OrderBy(static member => member.Name, StringComparer.Ordinal).ToArray()),
                Constructors = Array.AsReadOnly(constructors),
                EnumValues = Array.AsReadOnly(contract.EnumValues.OrderBy(static value => value.Name, StringComparer.Ordinal).ToArray()),
                GenericParameters = Array.AsReadOnly(generic)
            };
        }).OrderBy(static contract => contract.Type, StringComparer.Ordinal).ToArray());
    }

    private static ArgumentException Invalid(string message) => new("RGA0005: " + message);
}