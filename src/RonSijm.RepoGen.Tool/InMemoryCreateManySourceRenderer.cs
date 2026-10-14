namespace RonSijm.RepoGen.Tool;

internal static class InMemoryCreateManySourceRenderer
{
    internal static void AppendInputs(Action<string> line, ConfiguredCommandModel command)
    {
        var many = command.CreateMany!;
        line("var __repoGenCreateInput = global::RonSijm.RepoGen.Abstractions.RepositoryCreateInputs.Prepare<" + many.ItemTypeName + ">((" +
            many.ItemsSource + ") ?? throw new global::System.ArgumentNullException(\"items\"), " + many.MaximumItems + ", cancellationToken);");
        line("if (__repoGenCreateInput.Count == 0) return new global::RonSijm.RepoGen.Abstractions.CommandResult" +
            (command.ResultValueTypeName is null ? "" : "<" + command.ResultValueTypeName + ">") + "(global::RonSijm.RepoGen.Abstractions.CommandStatus.NoChanges);");
    }

    internal static void AppendBody(Action<string> line, EntityRepositoryModel entity, ConfiguredCommandModel command)
    {
        line("var __repoGenResult = await __repoGenData.CreateManyAsync<" + InMemoryCommandSourceRenderer.RowType(entity) + ", " +
            command.CreateMany!.ItemTypeName + ", " + entity.InMemoryStore!.KeyTypeName + ", " + (command.ResultTypeName ?? "int") + ">(");
        line("    __repoGenData." + InMemoryCommandSourceRenderer.StoreName(entity) + ", __repoGenCreateInput,");
        line("    item => " + (command.InMemoryCommand!.MappedCreateSource ?? command.CreateMany.CreateSource) + ",");
        if (command.SavedResultKeys is not null) InMemorySavedResultSourceRenderer.AppendResult(line, entity, command);
        else line("    entity => " + (command.ResultSource ?? "0") + ",");
        line("    " + (command.SaveChanges ? "true" : "false") + ", cancellationToken).ConfigureAwait(false);");
        line("return __repoGenResult" + (command.ResultTypeName is null ? ".WithoutValue()" : "") + ";");
    }
}
