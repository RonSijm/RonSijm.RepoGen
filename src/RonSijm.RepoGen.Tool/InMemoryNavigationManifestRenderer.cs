using System.Text.Json.Nodes;

namespace RonSijm.RepoGen.Tool;

internal static class InMemoryNavigationManifestRenderer
{
    internal static JsonObject Relationship(InMemoryNavigationEdgeModel edge)
    {
        var receipt = new JsonObject
        {
            ["name"] = edge.Name, ["collection"] = edge.IsCollection, ["required"] = edge.Required,
            ["sourceKeys"] = edge.SourceKeySource, ["targetKeys"] = edge.TargetKeySource
        };
        if (edge.KeyComparerSource is { } comparer) receipt["keyComparison"] = Comparison(comparer);
        if (edge.Join is { } join)
        {
            receipt["join"] = new JsonObject
            {
                ["entity"] = join.EntityTypeName, ["sourceKeys"] = join.SourceKeySource, ["targetKeys"] = join.TargetKeySource,
                ["requiredSource"] = join.RequiredSource, ["requiredTarget"] = join.RequiredTarget,
                ["pairs"] = "UniqueOrdinaryStructuralKeys", ["order"] = "JoinSnapshotOrder",
                ["visibility"] = "NoImplicitRepoGenPolicyInjection"
            };
            if (join.SourceKeyComparerSource is { } source) receipt["join"]!["sourceKeyComparison"] = Comparison(source);
            if (join.TargetKeyComparerSource is { } target) receipt["join"]!["targetKeyComparison"] = Comparison(target);
            if (join.SourceKeyComparerSource is not null || join.TargetKeyComparerSource is not null)
                receipt["join"]!["pairs"] = "UniqueDeclaredComponentKeys";
            // Required edges keep their established receipt byte-for-byte. This is test-only
            // capability metadata, never part of the production API/cache shape.
            if (!join.RequiredSource || !join.RequiredTarget)
            {
                var optional = (JsonObject)receipt["join"]!;
                optional["optionalSource"] = join.RequiredSource ? "NotApplicable" : "NullOrUnmatchedKeysDoNotBelongToAnOwner";
                optional["optionalTarget"] = join.RequiredTarget ? "NotApplicable" : join.PreserveMissingTargets ? "PreserveMissingTargetPerJoinRow" : "ExplicitLeadingNonNullFilter";
                optional["nullPropagation"] = join.PreserveMissingTargets ? "ExplicitNullSafeClrExpressionsOnly" : "NotSimulated";
                if (join.PreserveMissingTargets) optional["missingTargetPolicy"] = "OptInNullSlotsNotSqlNullPropagationOrExplicitLoading";
            }
        }
        return receipt;
    }

    private static JsonObject Comparison(string source) => new()
    {
        ["factory"] = source, ["scope"] = "FixtureReadRelationshipsAndUniquenessOnly",
        ["nullKeys"] = "NeverJoin", ["queryStoreAndWriteEquality"] = "Unchanged", ["providerEquivalence"] = "NotInferred"
    };
}
