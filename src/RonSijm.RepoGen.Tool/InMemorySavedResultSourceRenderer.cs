namespace RonSijm.RepoGen.Tool;

/// <summary>Runs the declared result read after shared save, outside the write/rollback boundary.</summary>
internal static class InMemorySavedResultSourceRenderer
{
    internal static void AppendResult(Action<string> line, EntityRepositoryModel entity, ConfiguredCommandModel command)
    {
        var batch = command.Batch is not null || command.CreateMany is not null;
        line(batch ? "    __repoGenSavedEntities =>" : "    __repoGenSavedEntity =>");
        line("    {");
        void Body(string value) => line("        " + value);
        var navigation = command.InMemoryCommand!.SavedResult!.Navigation;
        string rows;
        if (navigation is not null)
        {
            InMemoryNavigationSourceRenderer.AppendSnapshot(Body, "__repoGenData", navigation);
            rows = InMemoryNavigationSourceRenderer.Rows(navigation, entity.EntityTypeName);
        }
        else
        {
            rows = "__repoGenData." + RepositoryModelFactory.EscapeIdentifier(entity.EntityName + "Store") + ".Snapshot()";
            if (entity.InMemoryStore!.EntityMapping is { } mapping)
                rows = "global::System.Linq.Enumerable.Select(" + rows + ", " + mapping.ExpressionSource + ")";
        }
        Body("cancellationToken.ThrowIfCancellationRequested();");
        Body("var __repoGenSavedRows = global::RonSijm.RepoGen.Testing.InMemoryQueryExecution.WithCancellation(" + rows + ", cancellationToken);");
        if (command.InMemoryCommand.ModelQueryFilter is { } filter &&
            navigation?.Roots.Any(root => root.EntityTypeName == entity.EntityTypeName && root.ModelQueryFilter is not null) != true)
            Body("__repoGenSavedRows = global::System.Linq.Enumerable.Where(__repoGenSavedRows, " + filter.Root.PredicateSource + ");");
        foreach (var policy in (entity.VisibilityPolicies ?? []).Where(policy => command.BypassedVisibility?.Contains(policy.Name, StringComparer.Ordinal) != true))
            Body("__repoGenSavedRows = global::System.Linq.Enumerable.Where(__repoGenSavedRows, entity => " + policy.PredicateSource + ");");
        if (batch)
        {
            Body("return global::RonSijm.RepoGen.Testing.InMemorySavedBatchResults.Project(__repoGenSavedEntities, __repoGenSavedRows,");
            Body("    entity => new global::System.Object?[] { " + string.Join(", ", command.SavedResultKeys!.Select(key => "entity." + RepositoryModelFactory.EscapeIdentifier(key))) + " },");
            Body("    entity => " + command.ResultSource + ", cancellationToken);");
            line("    },");
            return;
        }
        Body("var entity = global::System.Linq.Enumerable.Single(__repoGenSavedRows, __repoGenRow => " + string.Join(" && ",
            command.SavedResultKeys!.Select(key => "global::RonSijm.RepoGen.Testing.InMemoryValueEquality.AreEqual(__repoGenRow." +
                RepositoryModelFactory.EscapeIdentifier(key) + ", __repoGenSavedEntity." + RepositoryModelFactory.EscapeIdentifier(key) + ")")) + ");");
        Body("return " + command.ResultSource + ";");
        line("    },");
    }
}
