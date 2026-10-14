using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static string[]? ValidateSavedCommandResult(IEntityType entity, RepositoryCommandDesign command)
    {
        if (!command.ResultFromSaved) return null;
        InvalidOperationException Invalid(string reason) => new("RGD3701: ReturnsSaved on '" + command.MethodName + "': " + reason);
        if (command.Kind is not RepositoryCommandKind.Create and not RepositoryCommandKind.Patch ||
            command.SaveChanges != true || command.ResultProjection is null)
            throw Invalid("Requires tracked Create/Patch or bounded CreateMany/PatchMany, explicit Save() and a result projection; Stage, Delete and immediate writes are excluded.");
        if (command.ReloadAfterSave)
            throw Invalid("The saved query already reads fresh values; do not combine it with ReloadAfterSave.");
        var keys = entity.FindPrimaryKey()?.Properties;
        if (keys is null || keys.Count == 0 || keys.Any(static key => key.PropertyInfo?.GetMethod?.IsPublic != true))
            throw Invalid("Requires a complete primary key with public readable CLR properties; shadow/backing-field keys are excluded.");
        return keys.Select(static key => key.PropertyInfo!.Name).ToArray();
    }
}
