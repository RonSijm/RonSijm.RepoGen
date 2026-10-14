using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static class InMemoryAtomicCommandSourceRenderer
{
    internal static void AppendBody(Action<string> line, EntityRepositoryModel entity, ConfiguredCommandModel command)
    {
        var upsert = command.Kind == RepositoryCommandKind.InsertOrUpdate;
        var method = upsert ? "ExecuteInsertOrUpdateAsync" : "ExecuteInsertIfAbsentAsync";
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
        line("    entity => " + (command.ResultSource ?? "0") + ",");
        line("    cancellationToken: cancellationToken).ConfigureAwait(false);");
        line("return __repoGenResult" + (command.ResultTypeName is null ? ".WithoutValue()" : "") + ";");
    }
}
