using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static class InMemoryImmediateCommandSourceRenderer
{
    internal static void AppendBody(Action<string> line, EntityRepositoryModel entity, ConfiguredCommandModel command)
    {
        var update = command.Kind == RepositoryCommandKind.Update;
        var method = "Execute" + (update ? "Update" : "Delete") + (command.IsReturning ? "Returning" : "") + "Async";
        var types = InMemoryCommandSourceRenderer.RowType(entity) + ", " + entity.InMemoryStore!.KeyTypeName +
            (command.IsReturning ? ", " + command.ResultTypeName : "");
        line("var __repoGenResult = await __repoGenData." + method + "<" + types + ">(");
        line("    __repoGenData." + InMemoryCommandSourceRenderer.StoreName(entity) + ",");
        // The production factory appends native expected-version predicates last. Use structural
        // scalar/binary token equality here instead of CLR byte-array reference equality.
        line("    entity => " + Predicate(command) + ",");
        if (update)
            AppendUpdate(line, command);
        if (command.IsReturning) line("    entity => " + command.ResultSource + ",");
        line("    cancellationToken: cancellationToken).ConfigureAwait(false);");
        line(command.WithOutcome
            ? "return new global::RonSijm.RepoGen.Abstractions.CommandResult(__repoGenResult == 0 ? global::RonSijm.RepoGen.Abstractions.CommandStatus.NotMatched : global::RonSijm.RepoGen.Abstractions.CommandStatus.Applied, __repoGenResult);"
            : "return __repoGenResult;");
    }

    internal static string Predicate(ConfiguredCommandModel command)
    {
        var versions = command.ExpectedVersions ?? [];
        var predicates = command.Predicates.Take(command.Predicates.Count - versions.Count).Select(static predicate => "(" + predicate.PredicateSource + ")")
            .Concat(versions.Select(static version => "global::RonSijm.RepoGen.Testing.InMemoryValueEquality.AreEqual(entity." +
                RepositoryModelFactory.EscapeIdentifier(version.PropertyName) + ", " + version.ValueSource + ")")).ToArray();
        return predicates.Length == 0 ? "true" : string.Join(" && ", predicates);
    }

    internal static void AppendUpdate(Action<string> line, ConfiguredCommandModel command)
    {
        line("    (entity, __repoGenCandidate) =>");
        line("    {");
        foreach (var assignment in command.InMemoryCommand!.ImmediateAssignments!)
            line("        __repoGenCandidate." + RepositoryModelFactory.EscapeIdentifier(assignment.PropertyName) + " = " + assignment.ValueSource + ";");
        line("    },");
    }
}
