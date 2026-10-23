using System.Reflection;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

/// <summary>One formatter for CLR overload identities and explicitly/reflection-annotated contract types.</summary>
internal static class RepositoryTypeName
{
    public static string Clr(Type type) => WithChildren(type, Clr);

    internal static string WithChildren(Type type, Func<Type, string> format) => Render(type, false,
        type.IsArray ? format(type.GetElementType()!) : null, type.IsGenericType ? type.GetGenericArguments().Select(format).ToArray() : []);

    public static string Annotated(RepositoryTypeNullabilityDesign shape) => Render(shape.Type, shape.NullableReference,
        shape.ElementType is null ? null : Annotated(shape.ElementType), shape.TypeArguments.Select(Annotated).ToArray());

    public static string Annotated(NullabilityInfo info, bool write = false)
    {
        // NullabilityInfo exposes the underlying struct's generic children for Nullable<Struct<T>>.
        if (Nullable.GetUnderlyingType(info.Type) is { } underlying)
        {
            return Render(underlying, false, null, info.GenericTypeArguments.Select(child => Annotated(child, write)).ToArray()) + "?";
        }

        return Render(info.Type, (write ? info.WriteState : info.ReadState) == NullabilityState.Nullable,
            info.ElementType is null ? null : Annotated(info.ElementType, write), info.GenericTypeArguments.Select(child => Annotated(child, write)).ToArray());
    }

    private static string Render(Type type, bool nullableReference, string? element, string[] arguments)
    {
        var suffix = nullableReference && !type.IsValueType ? "?" : string.Empty;
        if (type.IsGenericParameter)
        {
            return type.Name + suffix;
        }

        if (type.IsArray)
        {
            var item = element ?? Clr(type.GetElementType()!);
            var rank = "[" + new string(',', type.GetArrayRank() - 1) + "]";
            // Consecutive C# rank specifiers are outermost-first, unlike reflection's element walk.
            // A nullable inner array starts a new syntax node (T[]?[,]); append outside that node instead.
            if (type.GetElementType()!.IsArray && !item.EndsWith('?'))
            {
                var angleDepth = 0;
                for (var index = 0; index < item.Length; index++)
                {
                    if (item[index] == '<')
                    {
                        angleDepth++;
                    }
                    else if (item[index] == '>')
                    {
                        angleDepth--;
                    }
                    else if (item[index] == '[' && angleDepth == 0)
                    {
                        return item.Insert(index, rank) + suffix;
                    }
                }
            }

            return item + rank + suffix;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return (arguments.Length == 0 ? Clr(underlying) : arguments[0]) + "?";
        }

        var definition = (type.IsGenericType ? type.GetGenericTypeDefinition() : type).FullName ?? throw new InvalidOperationException($"Type '{type}' does not have a full name.");
        if (!type.IsGenericType)
        {
            return "global::" + definition.Replace('+', '.') + suffix;
        }

        // Reflection flattens enclosing and nested arguments; restore each segment's own arity.
        var offset = 0;
        var segments = definition.Split('+').Select(segment =>
        {
            var tick = segment.IndexOf('`');
            if (tick < 0)
            {
                return segment;
            }

            var arity = int.Parse(segment[(tick + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            var rendered = segment[..tick] + "<" + string.Join(", ", arguments.Skip(offset).Take(arity)) + ">";
            offset += arity;
            return rendered;
        });
        return "global::" + string.Join(".", segments) + suffix;
    }
}