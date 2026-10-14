using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool;

/// <summary>Checks the public type graph without executing application getters, constructors or methods.</summary>
internal sealed class RepositoryContractBoundary
{
    private const BindingFlags DeclaredPublic = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private readonly HashSet<Type> forbidden;
    private readonly HashSet<Type> forbiddenDefinitions;
    private readonly HashSet<Type> visited = [];

    public RepositoryContractBoundary(IEnumerable<Type> entityTypes)
    {
        forbidden = entityTypes.ToHashSet();
        forbiddenDefinitions = forbidden.Where(static entity => entity.IsGenericType).Select(static entity => entity.GetGenericTypeDefinition()).ToHashSet();
    }

    public void Validate(Type type, string path) => Visit(type, path, allowOpen: false);

    private void Visit(Type type, string path, bool allowOpen, bool dataShape = true)
    {
        if (!dataShape && (type.IsByRef || type.IsPointer))
        {
            Visit(type.GetElementType()!, path, allowOpen, dataShape: false);
            return;
        }

        if (!dataShape && type.IsFunctionPointer)
        {
            Visit(type.GetFunctionPointerReturnType(), path + ".return", allowOpen, dataShape: false);
            foreach (var parameter in type.GetFunctionPointerParameterTypes())
            {
                Visit(parameter, path + ".parameter", allowOpen, dataShape: false);
            }

            return;
        }

        if (type.IsGenericParameter && allowOpen)
        {
            if (!visited.Add(type))
            {
                return;
            }

            foreach (var constraint in type.GetGenericParameterConstraints().OrderBy(static constraint => constraint.ToString(), StringComparer.Ordinal))
            {
                Visit(constraint, path + ".constraint", allowOpen: true);
            }

            return;
        }

        if (dataShape && (type.IsByRef || type.IsPointer || type.IsFunctionPointer || type.IsByRefLike) ||
            !allowOpen && type.ContainsGenericParameters ||
            forbidden.Contains(type) || type.IsGenericType && forbiddenDefinitions.Contains(type.GetGenericTypeDefinition()) || typeof(DbContext).IsAssignableFrom(type) ||
            typeof(IQueryable).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type) ||
            typeof(IQueryProvider).IsAssignableFrom(type) || typeof(System.Linq.Expressions.Expression).IsAssignableFrom(type) ||
            typeof(System.Data.IDbConnection).IsAssignableFrom(type) || typeof(System.Data.IDbTransaction).IsAssignableFrom(type) ||
            typeof(System.Data.IDbCommand).IsAssignableFrom(type) || typeof(System.Data.IDataReader).IsAssignableFrom(type) ||
            Infrastructure(type) || !type.IsGenericParameter && !type.IsVisible)
        {
            throw Invalid(type, path);
        }

        if (type.HasElementType)
        {
            Visit(type.GetElementType()!, path + "[]", allowOpen, dataShape);
            return;
        }

        // Validate every closed argument, but expand each application definition only once.
        // Node<T>.Children : Node<List<T>> otherwise produces an unbounded reflected graph.
        var arguments = type.GetGenericArguments();
        for (var index = 0; index < arguments.Length; index++)
        {
            Visit(arguments[index], path + $".argument[{index}]", allowOpen, dataShape);
        }

        if (Framework(type) || type.IsEnum)
        {
            return;
        }

        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        if (!visited.Add(definition))
        {
            return;
        }

        if (definition.BaseType is { } parent)
        {
            Visit(parent, path + ".base", allowOpen: true);
        }

        foreach (var contract in definition.GetInterfaces().OrderBy(static contract => contract.ToString(), StringComparer.Ordinal))
        {
            Visit(contract, path + ".interface", allowOpen: true);
        }

        foreach (var parameter in definition.GetGenericArguments())
        {
            Visit(parameter, path + ".typeParameter", allowOpen: true);
        }

        foreach (var property in definition.GetProperties(DeclaredPublic).OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            Visit(property.PropertyType, path + "." + property.Name, allowOpen: true);
            foreach (var parameter in property.GetIndexParameters())
            {
                Visit(parameter.ParameterType, path + "." + property.Name + ".index[" + parameter.Name + "]", allowOpen: true);
            }
        }

        foreach (var field in definition.GetFields(DeclaredPublic).OrderBy(static field => field.Name, StringComparer.Ordinal))
        {
            Visit(field.FieldType, path + "." + field.Name, allowOpen: true);
        }

        foreach (var constructor in definition.GetConstructors(DeclaredPublic).OrderBy(static constructor => constructor.ToString(), StringComparer.Ordinal))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                Visit(parameter.ParameterType, path + ".ctor[" + parameter.Name + "]", allowOpen: true);
            }
        }

        foreach (var method in definition.GetMethods(DeclaredPublic).Where(static method => !method.IsSpecialName || method.Name.StartsWith("op_", StringComparison.Ordinal))
                     .OrderBy(static method => method.ToString(), StringComparer.Ordinal))
        {
            // Strong IDs may offer ordinary TryParse(out ...) / span helpers. Inspect their types
            // for ORM leaks without imposing operation/data-member ref restrictions on helper APIs.
            Visit(method.ReturnType, path + "." + method.Name + ".return", allowOpen: true, dataShape: false);
            foreach (var parameter in method.GetParameters())
            {
                Visit(parameter.ParameterType, path + "." + method.Name + "[" + parameter.Name + "]", allowOpen: true, dataShape: false);
            }

            foreach (var parameter in method.GetGenericArguments())
            {
                Visit(parameter, path + "." + method.Name + ".typeParameter", allowOpen: true);
            }
        }

        // Notification events are allowed, but their public callback signatures must not expose ORM objects.
        foreach (var notification in definition.GetEvents(DeclaredPublic).OrderBy(static notification => notification.Name, StringComparer.Ordinal))
        {
            if (notification.EventHandlerType?.GetMethod("Invoke") is not { } callback)
            {
                continue;
            }

            Visit(callback.ReturnType, path + "." + notification.Name + ".return", allowOpen: true, dataShape: false);
            foreach (var parameter in callback.GetParameters())
            {
                Visit(parameter.ParameterType, path + "." + notification.Name + "[" + parameter.Name + "]", allowOpen: true, dataShape: false);
            }
        }
    }

    private static bool Infrastructure(Type type)
    {
        var name = type.Assembly.GetName().Name ?? "";
        return name is "Microsoft.EntityFrameworkCore" || name.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) ||
               name is "RonSijm.RepoGen.Design" or "RonSijm.RepoGen.Tool" or "RonSijm.RepoGen.Generation" or "RonSijm.RepoGen.EntityFrameworkCore" or
                   "RonSijm.RepoGen.Caching" or "RonSijm.RepoGen.SqlServer" or "RonSijm.RepoGen.Sqlite" or "RonSijm.RepoGen.Validation";
    }

    private static bool Framework(Type type)
    {
        // A user type in a System.* namespace is not automatically a trusted framework leaf.
        var name = type.Assembly.GetName().Name ?? "";
        var frameworkName = name is "mscorlib" or "netstandard" or "System" or "Microsoft.CSharp" || name.StartsWith("System.", StringComparison.Ordinal);
        var token = Convert.ToHexString(type.Assembly.GetName().GetPublicKeyToken() ?? []);
        return frameworkName && token is "B03F5F7F11D50A3A" or "B77A5C561934E089" or "7CEC85D7BEA7798E" or "CC7B13FFCD2DDD51";
    }

    private static InvalidOperationException Invalid(Type type, string path) => new(
        $"RGD1502: Closed repository contract type '{type}' is not a public EF-free application type at '{path}'. Configure a DTO projection or a public request type without ORM, query, delegate or ref-only shapes.");
}