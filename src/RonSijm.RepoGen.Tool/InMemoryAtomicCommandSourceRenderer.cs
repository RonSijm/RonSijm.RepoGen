using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static class InMemoryAtomicCommandSourceRenderer
{
    internal static void AppendBody(Action<string> line, EntityRepositoryModel entity, ConfiguredCommandModel command)
    {
        var upsert = command.Kind == RepositoryCommandKind.InsertOrUpdate;
        var saved = command.SavedResultKeys is not null;
        var method = saved ? upsert ? "ExecuteInsertOrUpdateWithSavedResultAsync" : "ExecuteInsertIfAbsentWithSavedResultAsync" :
            upsert ? "ExecuteInsertOrUpdateAsync" : "ExecuteInsertIfAbsentAsync";
        line("var __repoGenResult = await __repoGenData." + method + "<" + InMemoryCommandSourceRenderer.RowType(entity) + ", " +
            entity.InMemoryStore!.KeyTypeName + ", " + (command.ResultTypeName ?? "int") + ">(");
        line("    __repoGenData." + InMemoryCommandSourceRenderer.StoreName(entity) + ",");
        line("    () =>");
        line("    {");
        line("        var __repoGenInput = " + command.InMemoryCommand!.MappedCreateSource + ";");
        foreach (var column in command.ConflictColumns!)
            line("        if ((object?)__repoGenInput." + RepositoryModelFactory.EscapeIdentifier(column) + " is null) throw new global::System.InvalidOperationException(\"A fixture conflict target cannot be null.\");");
        line("        return __repoGenInput;");
        line("    },");
        line("    static (entity, __repoGenInput) => " + string.Join(" && ", command.ConflictColumns.Select(static column =>
            "global::RonSijm.RepoGen.Testing.InMemoryValueEquality.AreEqual(entity." + RepositoryModelFactory.EscapeIdentifier(column) +
            ", __repoGenInput." + RepositoryModelFactory.EscapeIdentifier(column) + ")")) + ",");
        if (upsert)
        {
            line("    entity => " + InMemoryImmediateCommandSourceRenderer.Predicate(command) + ",");
            InMemoryImmediateCommandSourceRenderer.AppendUpdate(line, command);
        }
        if (saved)
        {
            var names = (command.InMemoryCommand!.SavedResult!.Navigation?.Roots.Select(static root => root.EntityTypeName) ?? [])
                .Prepend(entity.EntityTypeName).Distinct(StringComparer.Ordinal);
            line("    new string[] { " + string.Join(", ", names.Select(static name => System.Text.Json.JsonSerializer.Serialize(name))) + " },");
            InMemorySavedResultSourceRenderer.AppendResult(line, entity, command, suppliedSnapshot: true);
        }
        else
        {
            line("    entity => " + (command.ResultSource ?? "0") + ",");
        }
        if (upsert && command.InMemoryCommand!.ModelQueryFilter is { } filter)
            line("    updatedResultFilter: " + filter.Root.PredicateSource + ",");
        line("    cancellationToken: cancellationToken).ConfigureAwait(false);");
        line("return __repoGenResult" + (command.ResultTypeName is null ? ".WithoutValue()" : "") + ";");
    }
}
