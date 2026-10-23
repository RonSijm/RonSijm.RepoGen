using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static class InMemoryBatchCommandSourceRenderer
{
    internal static void AppendInputs(Action<string> line, ConfiguredCommandModel command)
    {
        var batch = command.Batch!;
        line("var __repoGenBatchInput = global::RonSijm.RepoGen.Abstractions.RepositoryBatchInputs.Prepare<" + batch.ItemTypeName + ", " + batch.KeyTypeName + ">((" +
            batch.ItemsSource + ") ?? throw new global::System.ArgumentNullException(\"items\"), item => " + batch.ItemKeySource + ", " + batch.MaximumItems + ", cancellationToken);");
        line("if (__repoGenBatchInput.Items.Count == 0) return new global::RonSijm.RepoGen.Abstractions.CommandResult" +
            (command.ResultValueTypeName is null ? "" : "<" + command.ResultValueTypeName + ">") + "(global::RonSijm.RepoGen.Abstractions.CommandStatus.NoChanges);");
    }

    internal static void AppendBody(Action<string> line, EntityRepositoryModel entity, ConfiguredCommandModel command)
    {
        var batch = command.Batch!;
        var coverage = command.InMemoryCommand!;
        line("var __repoGenResult = await __repoGenData.ExecuteBatchAsync<" + InMemoryCommandSourceRenderer.RowType(entity) + ", " + batch.ItemTypeName + ", " + entity.InMemoryStore!.KeyTypeName +
            ", " + batch.KeyTypeName + ", " + (command.ResultTypeName ?? "int") + ">(");
        line("    __repoGenData." + InMemoryCommandSourceRenderer.StoreName(entity) + ", __repoGenBatchInput,");
        line("    entity => " + (command.Predicates.Count == 0 ? "true" : string.Join(" && ", command.Predicates.Select(static predicate => "(" + predicate.PredicateSource + ")"))) + ",");
        line("    entity => " + batch.EntityKeySource + ",");
        if (command.Kind == RepositoryCommandKind.TrackedDelete) line("    null,");
        else
        {
            line("    (entity, item) =>");
            line("    {");
            line("        var __repoGenAssigned = new global::System.Collections.Generic.List<string>();");
            foreach (var update in batch.Updates)
            {
                if (update.ConditionSource is not null) line("        if (" + update.ConditionSource + ")");
                line("        {");
                line("            entity." + InMemoryWriteState.Access(update.PropertyName!, coverage.MappedPropertyState) + " = " + update.ValueBodySource + ";");
                line("            __repoGenAssigned.Add(" + System.Text.Json.JsonSerializer.Serialize(update.PropertyName) + ");");
                line("        }");
            }
            line("        return __repoGenAssigned;");
            line("    },");
        }
        line("    (entity, item) => " + (batch.ExpectedVersions.Count == 0 ? "true" : string.Join(" && ", batch.ExpectedVersions.Select(version =>
            InMemoryValueEqualitySourceRenderer.Compare(coverage.ValueEqualities, version.PropertyName,
                "entity." + InMemoryWriteState.Access(version.PropertyName, coverage.MappedPropertyState), version.ValueSource)))) + ",");
        line("    " + coverage.SameValuesSource + ",");
        line("    " + coverage.SameTokensSource + ",");
        InMemoryCommandSourceRenderer.AppendMerge(line, coverage);
        if (coverage.SavedResult is not null) InMemorySavedResultSourceRenderer.AppendResult(line, entity, command);
        else line("    entity => " + (command.ResultSource ?? "0") + ",");
        line("    delete: " + (command.Kind == RepositoryCommandKind.TrackedDelete ? "true" : "false") + ", save: " + (command.SaveChanges ? "true" : "false") + ",");
        if (command.Kind == RepositoryCommandKind.Patch && coverage.ValueEqualities is { Count: > 0 } equalities)
            line("    sameAssignedValue: " + InMemoryValueEqualitySourceRenderer.Assigned(equalities, coverage.MappedPropertyState) + ",");
        line("    forceWrite: " + (batch.ExpectedVersions.Count != 0 ? "true" : "false") + ", cancellationToken: cancellationToken).ConfigureAwait(false);");
        line("return __repoGenResult" + (command.ResultTypeName is null ? ".WithoutValue()" : "") + ";");
    }
}
