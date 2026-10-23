using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Internal;

namespace RonSijm.RepoGen.EntityFrameworkCore;

internal static class ReturningMappedMaterializer
{
    internal static Action<object, object?>?[] CreatePlan(IReadOnlyList<IProperty> properties) => properties.Select(Assignment).ToArray();

    private static Action<object, object?>? Assignment(IProperty property)
    {
        if (property.IsShadowProperty())
        {
            return null;
        }
        switch (ReturningPropertyAccess.MaterializationMember(property))
        {
            case FieldInfo field:
                return field.SetValue;
            case PropertyInfo member:
                object?[]? keys = property.IsIndexerProperty() ? [property.Name] : null;
                return (entity, value) => member.SetValue(entity, value, keys);
            default:
                throw new NotSupportedException("The returned scalar has no supported materialization member.");
        }
    }
}
