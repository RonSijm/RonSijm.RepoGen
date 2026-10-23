using System.Text.Json;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static class InMemoryCollectionCommandSourceRenderer
{
    internal static void AppendInputs(Action<string> line, ConfiguredCommandModel command)
    {
        for (var index = 0; index < command.Collections!.Count; index++)
        {
            var collection = command.Collections[index];
            line("var __repoGenCollection" + index + " = " + (collection.ConditionSource is null ? "" : "(" + collection.ConditionSource + ") ? ") +
                "global::RonSijm.RepoGen.Abstractions.RepositoryBatchInputs.Prepare<" + collection.ItemTypeName + ", " + collection.KeyTypeName + ">((" +
                collection.ItemsSource + ") ?? throw new global::System.ArgumentNullException(\"items\"), item => " + collection.ItemKeySource + ", " + collection.MaximumItems +
                ", cancellationToken)" + (collection.ConditionSource is null ? "" : " : null") + ";");
        }
    }

    internal static void AppendPlans(Action<string> line, EntityRepositoryModel entity, ConfiguredCommandModel command)
    {
        line("    new global::RonSijm.RepoGen.Testing.InMemoryCollectionWrite<" + InMemoryCommandSourceRenderer.RowType(entity) + ">[]");
        line("    {");
        for (var index = 0; index < command.InMemoryCommand!.Collections!.Count; index++)
        {
            var model = command.InMemoryCommand.Collections[index];
            var collection = command.Collections![index];
            line("        __repoGenData.ReconcileCollection<" + InMemoryCommandSourceRenderer.RowType(entity) + ", " + model.RowTypeName + ", " +
                collection.ItemTypeName + ", " + model.StoreKeyTypeName + ", " + collection.KeyTypeName + ">(");
            line("            __repoGenData." + model.StoreName + ", __repoGenCollection" + index + ",");
            line("            " + model.BelongsToSource + ",");
            var visible = collection.VisibilityPredicates.Count == 0 ? "true" : string.Join(" && ", collection.VisibilityPredicates.Select(static predicate => "(" + predicate + ")"));
            if (model.State.ModelQueryFilter is { } filter)
                visible = "(" + filter.PredicateBodySource + ")" + (collection.VisibilityPredicates.Count == 0 ? "" : " && (" + visible + ")");
            line("            child => " + visible + ",");
            line("            child => " + collection.ChildKeySource + ",");
            line("            " + OptionalLinkReferenceSourceRenderer.CreateLambda(model.CreateSource, collection.Link) + ",");
            line("            " + model.BindOwnerSource + ",");
            if (collection.Updates.Count == 0) line("            null,");
            else
            {
                line("            (child, item) =>");
                line("            {");
                line("                var __repoGenAssigned = new global::System.Collections.Generic.List<string>();");
                foreach (var update in collection.Updates)
                {
                    if (update.ConditionSource is not null) line("                if (" + update.ConditionSource + ")");
                    line("                {");
                    line("                    child." + InMemoryWriteState.Access(update.PropertyName!, model.State.MappedPropertyState) + " = " + update.ValueBodySource + ";");
                    line("                    __repoGenAssigned.Add(" + JsonSerializer.Serialize(update.PropertyName) + ");");
                    line("                }");
                }
                line("                return __repoGenAssigned;");
                line("            },");
            }
            line("            (child, item) => " + (collection.ExpectedVersions.Count == 0 ? "true" : string.Join(" && ", collection.ExpectedVersions.Select(version =>
                InMemoryValueEqualitySourceRenderer.Compare(model.State.ValueEqualities, version.PropertyName,
                    "child." + InMemoryWriteState.Access(version.PropertyName, model.State.MappedPropertyState), version.ValueSource)))) + ",");
            line("            " + model.State.SameValuesSource + ",");
            line("            " + model.State.SameTokensSource + ",");
            InMemoryCommandSourceRenderer.AppendMerge(value => line("        " + value), model.State);
            line("            maximumItems: " + collection.MaximumItems + ", deleteMissing: " + (collection.MissingItems == RepositoryCollectionRemovalBehavior.Delete ? "true" : "false") +
                ", rejectMissing: " + (collection.MissingItems == RepositoryCollectionRemovalBehavior.Reject ? "true" : "false") +
                ", forceWrite: " + (collection.ExpectedVersions.Count != 0 ? "true" : "false") +
                (OptionalLinkReferenceSourceRenderer.RequiresPreparation(model.CreateSource, collection.Link) ? ", prepareCreateBeforeVersions: true" : "") +
                (model.State.ValueEqualities is { Count: > 0 } equalities ? ", sameAssignedValue: " + InMemoryValueEqualitySourceRenderer.Assigned(equalities, model.State.MappedPropertyState) : "") +
                (collection.Link is { SharedReferenceColumns.Count: > 0 } link ? ", matchesOwner: " + SharedReferenceColumnSourceRenderer.Predicate(link, fixture: true) : "") +
                (model.CreateSource is not null && SharedReferenceColumnSourceRenderer.HasCreatedOwnerGuard(collection.Link) ? ", matchesCreatedOwner: " + SharedReferenceColumnSourceRenderer.CreatedOwnerPredicate(collection.Link!, fixture: true) : "") + "),");
        }
        line("    },");
    }
}
