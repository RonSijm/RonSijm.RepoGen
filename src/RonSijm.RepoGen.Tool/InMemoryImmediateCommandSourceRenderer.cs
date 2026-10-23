using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static class InMemoryImmediateCommandSourceRenderer
{
    internal static void AppendBody(Action<string> line, EntityRepositoryModel entity, ConfiguredCommandModel command)
    {
        var update = command.Kind == RepositoryCommandKind.Update;
        var fresh = command.SavedResultMaximumRows is not null;
        var method = fresh ? "ExecuteUpdateWithSavedResultsAsync" : "Execute" + (update ? "Update" : "Delete") + (command.IsReturning ? "Returning" : "") + "Async";
        var types = InMemoryCommandSourceRenderer.RowType(entity) + ", " + entity.InMemoryStore!.KeyTypeName +
            (command.IsReturning ? ", " + command.ResultTypeName : "");
        line("var __repoGenResult = await __repoGenData." + method + "<" + types + ">(");
        line("    __repoGenData." + InMemoryCommandSourceRenderer.StoreName(entity) + ",");
        // The production factory appends native expected-version predicates last. Use structural
        // scalar/binary token equality here instead of CLR byte-array reference equality.
        line("    entity => " + Predicate(command) + ",");
        if (update)
            AppendUpdate(line, command);
        if (fresh)
        {
            line("    " + command.SavedResultMaximumRows + ",");
            var names = (command.InMemoryCommand!.SavedResult!.Navigation?.Roots.Select(static root => root.EntityTypeName) ?? [])
                .Prepend(entity.EntityTypeName).Distinct(StringComparer.Ordinal);
            line("    new string[] { " + string.Join(", ", names.Select(static name => System.Text.Json.JsonSerializer.Serialize(name))) + " },");
            InMemorySavedResultSourceRenderer.AppendResult(line, entity, command, suppliedSnapshot: true);
        }
        else if (command.IsReturning)
        {
            line("    entity => " + command.ResultSource + ",");
        }
        line("    cancellationToken: cancellationToken).ConfigureAwait(false);");
        line(command.WithOutcome
            ? "return new global::RonSijm.RepoGen.Abstractions.CommandResult(__repoGenResult == 0 ? global::RonSijm.RepoGen.Abstractions.CommandStatus.NotMatched : global::RonSijm.RepoGen.Abstractions.CommandStatus.Applied, __repoGenResult);"
            : "return __repoGenResult;");
    }

    internal static string Predicate(ConfiguredCommandModel command)
    {
        var versions = command.ExpectedVersions ?? [];
        var predicates = command.Predicates.Take(command.Predicates.Count - versions.Count).Select(static predicate => "(" + predicate.PredicateSource + ")")
            .Concat(versions.Select(version => "global::RonSijm.RepoGen.Testing.InMemoryValueEquality.AreEqual(entity." +
                InMemoryWriteState.Access(version.PropertyName, command.InMemoryCommand?.MappedPropertyState) + ", " + version.ValueSource + ")")).ToArray();
        return predicates.Length == 0 ? "true" : string.Join(" && ", predicates);
    }

    internal static void AppendUpdate(Action<string> line, ConfiguredCommandModel command)
    {
        line("    (entity, __repoGenCandidate) =>");
        line("    {");
        foreach (var assignment in command.InMemoryCommand!.ImmediateAssignments!)
            line("        __repoGenCandidate." + InMemoryWriteState.Access(assignment.PropertyName, command.InMemoryCommand?.MappedPropertyState) + " = " + assignment.ValueSource + ";");
        line("    },");
    }
}
