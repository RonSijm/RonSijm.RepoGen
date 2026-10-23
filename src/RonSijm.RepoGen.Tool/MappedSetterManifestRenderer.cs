using System.Text.Json;
using System.Text.Json.Nodes;

namespace RonSijm.RepoGen.Tool;

internal static class MappedSetterManifestRenderer
{
    internal static void Append(JsonNode command, ConfiguredCommandModel model)
    {
        MappedReconciliationManifestRenderer.Append(command, model);
        if (model.Batch is { } batch && batch.Updates.Any(static update => update.MappedProperty is not null))
            command["mappedBatchUpdateTargets"] = JsonSerializer.SerializeToNode(new
            {
                optIn = "PatchMany.SetProperty<T>(mappedPropertyName, value)",
                properties = batch.Updates.Where(static update => update.MappedProperty is not null).Select(static update => new
                {
                    name = update.MappedProperty!.Name, type = update.MappedProperty.TypeName, shadow = update.MappedProperty.IsShadow
                }),
                names = "ExactFinalModelMetadataNotServiceInputs",
                execution = "EntryCurrentValueUnderBoundedBatchSnapshotStageSave",
                identityChanges = "MatchingKeysForeignKeysAndUniqueIdentityExcluded",
                generatedTargets = "ExcludedEvenWithAllowWrite",
                fixture = "IndependentCompleteWriteStateBindingsOrTypedOverride",
                nativeReturningOrGraphTargets = false
            });
        var setters = model.Updates.Where(static update => update.MappedProperty is not null).ToArray();
        if (setters.Length == 0) return;
        command["mappedUpdateTargets"] = JsonSerializer.SerializeToNode(new
        {
            optIn = "SetProperty<T>(mappedPropertyName, value)",
            properties = setters.Select(static update => new
            {
                name = update.MappedProperty!.Name, type = update.MappedProperty.TypeName, shadow = update.MappedProperty.IsShadow
            }),
            names = "ExactFinalModelMetadataNotServiceInputs",
            execution = model.IsTracked ? "EntryCurrentValueUnderTrackedSnapshotStageSave" : "EfTranslatedSetBasedUpdateNoSharedSave",
            identityChanges = "KeysForeignKeysAndUniqueIdentityExcluded",
            generatedTargets = "ExcludedEvenWithAllowWrite",
            fixture = "IndependentCompleteWriteStateBindingsOrTypedOverride",
            nativeReturningOrAtomicTargets = false
        });
    }
}
