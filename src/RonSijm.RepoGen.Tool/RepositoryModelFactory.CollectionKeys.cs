using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static IProperty[] CollectionMatchingProperties(IEntityType child, INavigation navigation,
        RepositoryCollectionReconciliationDesign collection)
    {
        var selector = collection.ChildKey;
        var components = selector.Body is NewExpression creation ? creation.Arguments.ToArray() : [selector.Body];
        if (selector.Body is NewExpression tuple && (!IsCollectionValueTuple(tuple.Type) || tuple.Arguments.Count != tuple.Type.GenericTypeArguments.Length))
            throw new InvalidOperationException("RGD2801: Composite child matching requires an explicit ValueTuple construction with two to seven direct mapped scalar components, not a custom key constructor or nested tuple.");
        if (components.Any(component => component is not MemberExpression { Expression: ParameterExpression parameter } member ||
                parameter != selector.Parameters[0] || child.FindProperty(member.Member.Name) is null))
            throw new InvalidOperationException("RGD2801: Each child matching component must select a direct mapped scalar property, not a navigation, conversion or computed selector.");

        var keys = components.Select(component => CommandProperty(child, Expression.Lambda(component, selector.Parameters))).ToArray();
        if (keys.Length > 1 && keys.Any(static key => IsNestedCollectionTuple(key.ClrType)))
            throw new InvalidOperationException("RGD2801: Composite child matching cannot contain nested ValueTuple components, even when mapped through a scalar converter.");
        var scoped = navigation.ForeignKey.Properties.Concat(keys).ToHashSet();
        bool ProvesUnique(IReadOnlyList<IProperty> properties) => properties.Any(keys.Contains) && properties.All(scoped.Contains);
        var unique = child.GetKeys().Any(candidate => ProvesUnique(candidate.Properties)) ||
            child.GetIndexes().Any(candidate => candidate.IsUnique && candidate.GetFilter() is null && ProvesUnique(candidate.Properties));
        if (collection.IsLinkReconciliation && !keys.SequenceEqual(ValidateLinkReferences(navigation, collection).Target.Properties))
            throw new InvalidOperationException("RGD3901: Link matching must select every target FK component in mapped order, never owner columns, payload, partial or computed keys.");
        if (keys.Distinct().Count() != keys.Length || !unique || keys.Any(key => key.IsNullable || key.ClrType.IsArray || key.IsForeignKey() && !collection.IsLinkReconciliation ||
                key.PropertyInfo?.GetMethod is null || collection.CreateMapping is not null && key.ValueGenerated != ValueGenerated.Never) ||
            components.Where((component, index) => component.Type != keys[index].ClrType).Any())
            throw new InvalidOperationException("RGD2801: Child matching requires distinct non-null mapped scalars, unique together globally or within this owner's FK. Added items require application-supplied (not store-generated) matching components.");
        return keys;
    }

    private static bool IsCollectionValueTuple(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(ValueTuple<,>) || definition == typeof(ValueTuple<,,>) || definition == typeof(ValueTuple<,,,>) ||
         definition == typeof(ValueTuple<,,,,>) || definition == typeof(ValueTuple<,,,,,>) || definition == typeof(ValueTuple<,,,,,,>));

    private static bool IsNestedCollectionTuple(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(ValueTuple) || IsCollectionValueTuple(type) || type.IsGenericType &&
            (type.GetGenericTypeDefinition() == typeof(ValueTuple<>) || type.GetGenericTypeDefinition() == typeof(ValueTuple<,,,,,,,>));
    }

    private static string CollectionKeySource(string source, IProperty[] keys) => keys.Length == 1 ? source :
        "global::RonSijm.RepoGen.Abstractions.RepositoryCollectionKeys.Require(" + source + ")";
}
