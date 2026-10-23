using System.Text.Json.Nodes;

namespace RonSijm.RepoGen.Tool;

internal static class MappedReturningManifestRenderer
{
    internal static void Append(JsonNode command, ConfiguredReturningStateModel? state)
    {
        if (state?.IncludeMappedProperties != true) return;
        var receipt = command["returningPropertyValues"]!;
        receipt["permission"] = "PerCommandIncludeMappedProperties";
        receipt["conversions"] = "ModelClrValuesIncludingFieldIndexerAndShadowNulls";
        receipt["materialization"] = "EfConfiguredWritableMemberNoTrackerEntry";
        receipt["memberReads"] = "MappedScalarBufferNotEntityGetterBehavior";
        for (var index = 0; index < state.Properties.Count; index++)
            receipt["properties"]![index]!["accessKind"] = state.Properties[index].AccessKind;
    }
}
