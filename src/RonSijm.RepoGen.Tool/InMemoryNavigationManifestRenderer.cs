using System.Text.Json.Nodes;

namespace RonSijm.RepoGen.Tool;

internal static class InMemoryNavigationManifestRenderer
{
    internal static JsonObject Relationship(InMemoryNavigationEdgeModel edge, InMemoryNavigationModel navigation)
    {
        var receipt = Relationship(edge);
        if (edge.Join is not { } join) return receipt;
        bool Filtered(string variable) => navigation.Roots.Any(root => root.VariableName == variable && root.ModelQueryFilter is not null);
        var source = Filtered(edge.SourceVariableName);
        var target = Filtered(edge.TargetVariableName);
        var link = Filtered(join.VariableName);
        if (source || target || link)
            receipt["join"]!["modelFilters"] = new JsonObject
            {
                ["source"] = source, ["target"] = target, ["join"] = link,
                ["application"] = "EachDatasetBeforeDeclaredLoading",
                ["requiredFilteredTarget"] = "OmitLinkNotOwner",
                ["optionalFilteredTarget"] = join.RequiredTarget ? "NotApplicable"
                    : join.PreserveMissingTargets ? "PreserveNullSlot" : "ExplicitLeadingNonNullFilter",
                ["validation"] = "UnfilteredPrincipalKeysAndVisibleJoinPairsNoProviderGuarantees"
            };
        return receipt;
    }

    internal static JsonObject Relationship(InMemoryNavigationEdgeModel edge)
    {
        var receipt = new JsonObject
        {
            ["name"] = edge.Name, ["collection"] = edge.IsCollection, ["required"] = edge.Required,
            ["sourceKeys"] = edge.SourceKeySource, ["targetKeys"] = edge.TargetKeySource
        };
        if (edge.KeyComparerSource is { } comparer) receipt["keyComparison"] = Comparison(comparer);
        if (edge.MappedPropertyKeys is { } bindings)
        {
            receipt["mappedPropertyKeys"] = System.Text.Json.JsonSerializer.SerializeToNode(new
            {
                optIn = "HasInMemoryNavigation(useMappedPropertyKeys: true)", properties = bindings,
                scope = "DetachedReadRelationshipMatchingAndUniquenessOnly", storeIdentity = "Unchanged",
                trackedState = "NotSimulated", providerEquivalence = "NotInferred"
            });
        }
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
