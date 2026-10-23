using System.Text.Json;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static IProperty MappedProperty(IEntityType entity, string name, Type type, string diagnostic)
    {
        var property = entity.FindProperty(name);
        if (property is null || property.ClrType != type)
            throw new InvalidOperationException(diagnostic + ": Mapped property '" + entity.ClrType.FullName + "." + name +
                "' must exist as an EF scalar with the exact declared CLR type '" + type + "'. Navigation/unknown/widened/boxed bindings are not supported.");
        return property;
    }

    private static string MappedPropertyRead(IProperty property) =>
        "global::Microsoft.EntityFrameworkCore.EF.Property<" + CSharpTypeName(property.ClrType, property.IsNullable) + ">(entity, " + JsonSerializer.Serialize(property.Name) + ")";

    private static MappedPropertyBindingModel PropertyBinding(IProperty property) => new(property.Name, CSharpTypeName(property.ClrType), property.IsShadowProperty());

    private static IProperty VersionProperty(IEntityType entity, RepositoryExpectedVersionDesign version, string diagnostic = "RGD1310") =>
        version.MappedPropertyName is { } name ? MappedProperty(entity, name, version.PropertyType, diagnostic)
            : CommandProperty(entity, version.Property!);

    // Deliberately conservative for generated method/parameter names; arbitrary mapped names remain quoted metadata.
    private static bool MappedBindingIdentifier(string name)
    {
        var value = name.StartsWith('@') ? name[1..] : name;
        return (name.StartsWith('@') || !CSharpKeywords.Contains(value)) && value.Length > 0 && (char.IsLetter(value[0]) || value[0] == '_') &&
               value.All(static character => char.IsLetterOrDigit(character) || character == '_');
    }
}
