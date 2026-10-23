using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

/// <summary>Model-aware, bounded result-data metadata inspection. Never evaluates a constructor or getter.</summary>
internal sealed class InMemoryProjectionDataShape
{
    internal HashSet<Type> Dtos { get; } = [];

    internal static InMemoryProjectionDataShape? Create(Type result, IModel? model)
    {
        result = Nullable.GetUnderlyingType(result) ?? result;
        if (!InMemoryProjectionTranslationInspector.DtoCandidate(result, model)) return null;
        var shape = new InMemoryProjectionDataShape();
        var visiting = new HashSet<Type>();
        var heights = new Dictionary<Type, int>();
        return Height(result, 0) is not null ? shape : null;

        int? Height(Type type, int depth)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (InMemoryTranslationExpressions.Scalar(type)) return 0;
            if (depth >= 16 || visiting.Contains(type)) return null;
            if (CollectionElement(type) is { } element)
                return CollectionElement(element) is null && Height(element, depth + 1) is { } elementHeight ? elementHeight + 1 : null;
            if (heights.TryGetValue(type, out var knownHeight)) return depth + knownHeight <= 16 ? knownHeight : null;
            if (shape.Dtos.Count + visiting.Count >= 128 || !InMemoryProjectionTranslationInspector.DtoCandidate(type, model)) return null;
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            if (properties.Length + fields.Length == 0 ||
                properties.Any(property => property.GetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0)) return null;
            visiting.Add(type);
            var height = 1;
            foreach (var member in properties.Select(static property => property.PropertyType).Concat(fields.Select(static field => field.FieldType)))
            {
                if (Height(member, depth + 1) is not { } memberHeight) return null;
                height = Math.Max(height, memberHeight + 1);
            }
            visiting.Remove(type);
            shape.Dtos.Add(type);
            heights.Add(type, height);
            return height;
        }
    }

    internal static Type? CollectionElement(Type type)
    {
        if (type.IsArray) return type.GetArrayRank() == 1 ? type.GetElementType() : null;
        if (!type.IsGenericType) return null;
        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(ICollection<>) ||
            definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>) || definition == typeof(IEnumerable<>)
            ? type.GetGenericArguments()[0] : null;
    }

    internal static bool ValueType(Type type, HashSet<Type> dtos)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return InMemoryTranslationExpressions.Scalar(type) || dtos.Contains(type) ||
            CollectionElement(type) is { } element &&
                (InMemoryTranslationExpressions.Scalar(element) || dtos.Contains(Nullable.GetUnderlyingType(element) ?? element));
    }

    internal static bool List(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
}
