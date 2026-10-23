using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Internal;

/// <summary>Shared generation/runtime boundary for explicitly captured mapped scalar state.</summary>
internal static class ReturningPropertyAccess
{
    internal static bool Ordinary(IProperty property) => !property.IsShadowProperty() && !property.IsIndexerProperty() &&
        property.PropertyInfo?.GetMethod is not null && property.PropertyInfo.SetMethod is not null;

    internal static MemberInfo MaterializationMember(IProperty property)
    {
        var member = property.GetMemberInfo(forMaterialization: true, forSet: true);
        if (member is FieldInfo { IsStatic: false, IsInitOnly: false, IsLiteral: false } field && field.FieldType == property.ClrType)
            return field;
        if (member is PropertyInfo { SetMethod.IsStatic: false } info &&
            (property.IsIndexerProperty() ? info.GetIndexParameters() is [{ ParameterType: var key }] && key == typeof(string) && info.PropertyType.IsAssignableFrom(property.ClrType)
                : info.GetIndexParameters().Length == 0 && info.PropertyType == property.ClrType))
            return info;
        throw new NotSupportedException("Returned mapped property '" + property.Name + "' requires a writable instance field or supported property/indexer materialization member; readonly/literal/static fields are not supported.");
    }

    internal static string Kind(IProperty property) => property.IsShadowProperty() ? "Shadow" : property.IsIndexerProperty() ? "Indexer" :
        property.PropertyInfo is null ? "FieldOnly" : MaterializationMember(property) is FieldInfo ? "FieldBacked" : "ClrProperty";
}
