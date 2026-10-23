using System.Text.Json;
using System.Text.Json.Nodes;

namespace RonSijm.RepoGen.Tool;

internal static class NativeSavedResultManifestRenderer
{
    internal static void Append(JsonNode command, ConfiguredCommandModel model)
    {
        if (model.SavedResultMaximumRows is not { } maximumRows)
        {
            return;
        }
        command["nativeSavedResultQuery"] = JsonSerializer.SerializeToNode(new
        {
            optIn = "ReturnsSavedMany", maximumRows,
            key = model.SavedResultKeys,
            selection = "DirectlyUpdatedCompletePrimaryKeysNotMutationPredicate",
            tracking = "AsNoTrackingNoSharedSave",
            transaction = "BeforeOwnedCommitOrBorrowedSavepointRelease",
            rowBound = "AbortAndRollbackNotTakeOrPreselection",
            cardinality = "ExactlyOneResultPerDirectlyUpdatedKeyNoSilentShortening",
            ordering = "Unspecified",
            filters = "RootModelFiltersAndDeclaredVisibilityUnlessBypassedRelatedVisibilityExplicit",
            failure = "OriginalExceptionNotRepositorySavedResultException",
            nativeReturningGraph = false
        });
        if (model.ReturningState is not null)
        {
            command["returningPropertyValues"]!["memberReads"] = "FreshQueryProjectionNotNativeCaptureBuffer";
        }
    }
}
