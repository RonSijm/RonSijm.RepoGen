using System.Text.Json;
using System.Text.Json.Nodes;

namespace RonSijm.RepoGen.Tool;

internal static class MappedReconciliationManifestRenderer
{
    internal static void Append(JsonNode command, ConfiguredCommandModel model)
    {
        var collections = model.Collections?.Where(static collection => collection.Updates.Any(static update => update.MappedProperty is not null)).ToArray();
        if (collections is not { Length: > 0 }) return;
        command["mappedReconciliationUpdateTargets"] = JsonSerializer.SerializeToNode(new
        {
            optIn = "ReconcileCollectionOrLinks.Update.SetProperty<T>(mappedPropertyName, value)",
            collections = collections.Select(static collection => new
            {
                navigation = collection.NavigationName,
                kind = collection.Link is null ? "LeafChildren" : "ExplicitLinkRows",
                maximumItems = collection.MaximumItems,
                properties = collection.Updates.Where(static update => update.MappedProperty is not null).Select(static update => new
                {
                    name = update.MappedProperty!.Name, type = update.MappedProperty.TypeName, shadow = update.MappedProperty.IsShadow
                })
            }),
            names = "ExactFinalModelMetadataNotServiceInputs",
            execution = "ChildEntryCurrentValueUnderBoundedReconciliationSnapshotsStageSave",
            permissions = "SeparateParentAndPerCollectionChildAllowWrite",
            identityChanges = "MatchingKeysOwnershipForeignKeysAndUniqueIdentityExcluded",
            generatedTargets = "ExcludedEvenWithAllowWrite",
            creation = "ExistingExplicitAddMappingUnchangedMatchedRowSettersNotAppliedToNewRows",
            fixture = "IndependentCompleteWriteStateForEachParticipantAndSimulateCollectionWritesOrTypedOverride",
            broaderGraphsOrNativeTargets = false
        });
    }
}
