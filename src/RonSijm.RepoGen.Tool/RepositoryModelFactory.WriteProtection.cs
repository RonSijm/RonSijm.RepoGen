using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static string[] CreateWriteProtection(IEntityType entity, RepositoryEntityDesign? design, RepositoryGenerationSettingsModel settings)
    {
        var properties = design?.ProtectedWriteProperties.ToArray() ?? [];
        if (properties.Length == 0)
        {
            return properties;
        }

        if (settings.ContractNamespace is null)
        {
            throw new InvalidOperationException($"RGD2901: ProtectWrite on '{entity.ClrType.FullName}' requires closed repositories. Public selectors/entities allow writes outside designer mappings.");
        }

        foreach (var name in properties)
        {
            if (entity.FindProperty(name) is null)
            {
                throw new InvalidOperationException($"RGD2902: Protected write property '{entity.ClrType.FullName}.{name}' must be a mapped scalar, not an ignored property or navigation.");
            }
        }

        foreach (var binding in design!.ProtectedMappedWriteProperties)
            MappedProperty(entity, binding.Key, binding.Value, "RGD1311");

        return properties;
    }

    private static string[]? WritePermissions(string[] protectedWrites, HashSet<string> allowed)
        => protectedWrites.Length == 0 ? null : protectedWrites.Where(allowed.Contains).Order(StringComparer.Ordinal).ToArray();

    private static void ValidateWriteProtection(IProperty property, string methodName, HashSet<string> allowed,
        string[]? protectedWrites)
    {
        if (protectedWrites?.Contains(property.Name, StringComparer.Ordinal) == true && !allowed.Contains(property.Name))
        {
            throw new InvalidOperationException($"RGD2903: Command '{methodName}' writes explicitly protected scalar '{property.DeclaringType.Name}.{property.Name}'. Declare AllowWrite() deliberately on that command or child mapping.");
        }
    }
}
