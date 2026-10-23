using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static string[]? ValidateSavedCommandResult(IEntityType entity, RepositoryCommandDesign command)
    {
        if (!command.ResultFromSaved)
        {
            return null;
        }
        InvalidOperationException Invalid(string reason) => new("RGD3701: " + (command.ReturnsMany ? "ReturnsSavedMany" : "ReturnsSaved") + " on '" + command.MethodName + "': " + reason);
        var atomic = command.Kind is RepositoryCommandKind.InsertIfAbsent or RepositoryCommandKind.InsertOrUpdate;
        var nativeMany = command.ReturnsMany && command.SavedResultMaximumRows is > 0;
        var tracked = command.Kind is RepositoryCommandKind.Create or RepositoryCommandKind.Patch;
        var validLifecycle = nativeMany ? command.Kind == RepositoryCommandKind.Update && command.SaveChanges is null
            : !command.ReturnsMany && (atomic ? command.SaveChanges is null : tracked && command.SaveChanges == true);
        if (command.ResultProjection is null || !validLifecycle)
        {
            throw Invalid("Requires saved tracked Create/Patch/CreateMany/PatchMany, native InsertIfAbsent/InsertOrUpdate, or Update with bounded ReturnsSavedMany. Native commands exclude Save/Stage; Delete is excluded.");
        }
        if (command.ReloadAfterSave)
        {
            throw Invalid("The saved query already reads fresh values; do not combine it with ReloadAfterSave.");
        }
        var keys = entity.FindPrimaryKey()?.Properties;
        if (keys is null || keys.Count == 0 || keys.Any(static key => key.PropertyInfo?.GetMethod?.IsPublic != true))
        {
            throw Invalid("Requires a complete primary key with public readable CLR properties; shadow/backing-field keys are excluded.");
        }
        return keys.Select(static key => key.PropertyInfo!.Name).ToArray();
    }
}
