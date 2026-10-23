using System.Text;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static class TrackedCommandSourceRenderer
{
    public static void AppendBody(StringBuilder builder, EntityRepositoryModel entity, ConfiguredCommandModel command,
        string indent, string work = "null", bool returnResult = true, bool inputsPrepared = false)
    {
        if (command.CreateMany is not null)
        {
            CreateManyCommandSourceRenderer.AppendBody(builder, entity, command, indent, work, returnResult, inputsPrepared);
            return;
        }
        if (command.Batch is not null)
        {
            BatchCommandSourceRenderer.AppendBody(builder, entity, command, indent, work, returnResult, inputsPrepared);
            return;
        }

        if (!inputsPrepared)
        {
            CollectionReconciliationSourceRenderer.AppendInputs(builder, command, indent);
        }

        builder.Append(indent).AppendLine("cancellationToken.ThrowIfCancellationRequested();");
        if (command.SaveChanges && command.Collections?.Any(static collection => collection.Link is not null) == true)
        {
            if (command.Updates.Count == 0)
                builder.Append(indent).Append("if (").Append(string.Join(" || ", command.Collections.Select((_, index) => "__repoGenCollection" + index + " is not null"))).AppendLine(")");
            builder.Append(indent).AppendLine("    global::RonSijm.RepoGen.EntityFrameworkCore.TrackedCollectionReconciler.ValidateSaveBoundary(context);");
        }
        if (command.Kind != RepositoryCommandKind.Create)
        {
            builder.Append(indent).Append("global::System.Linq.IQueryable<").Append(entity.EntityTypeName).Append("> query = context.Set<")
                .Append(entity.EntityTypeName).AppendLine(">();");
            foreach (var predicate in command.Predicates)
            {
                builder.Append(indent).Append("query = global::System.Linq.Queryable.Where(query, entity => ").Append(predicate.PredicateSource).AppendLine(");");
            }
        }

        var executor = "global::RonSijm.RepoGen.EntityFrameworkCore.TrackedCommandExecutor.";
        var method = command.Kind == RepositoryCommandKind.Create ? "CreateAsync" : command.Kind == RepositoryCommandKind.Patch
            ? command.Collections is null ? "PatchAsync" : "PatchWithCollectionsAsync" : "DeleteAsync";
        builder.Append(indent).Append("var result = await ").Append(executor).Append(method).Append('<').Append(entity.EntityTypeName)
            .Append(", ").Append(command.ResultTypeName ?? "int").AppendLine(">(");
        builder.Append(indent).AppendLine("    context,");
        if (command.Kind == RepositoryCommandKind.Create)
        {
            builder.Append(indent).Append("    () => ").Append(command.CreateSource).AppendLine(",");
        }
        else
        {
            builder.Append(indent).AppendLine("    query,");
            if (command.Kind == RepositoryCommandKind.Patch)
            {
                builder.Append(indent).AppendLine("    entity =>");
                builder.Append(indent).AppendLine("    {");
                builder.Append(indent).AppendLine("        var __repoGenAssigned = new global::System.Collections.Generic.List<string>();");
                foreach (var update in command.Updates)
                {
                    if (update.ConditionSource is not null)
                    {
                        builder.Append(indent).Append("        if (").Append(update.ConditionSource).AppendLine(")");
                    }

                    builder.Append(indent).AppendLine("        {");
                    builder.Append(indent).Append("            ");
                    if (update.MappedProperty is { } mapped)
                        builder.Append("context.Entry(entity).Property<").Append(mapped.TypeName).Append(">(")
                            .Append(System.Text.Json.JsonSerializer.Serialize(update.PropertyName)).Append(").CurrentValue");
                    else builder.Append("entity.").Append(RepositoryModelFactory.EscapeIdentifier(update.PropertyName!));
                    builder.Append(" = ").Append(update.ValueBodySource).AppendLine(";");
                    builder.Append(indent).Append("            __repoGenAssigned.Add(").Append(System.Text.Json.JsonSerializer.Serialize(update.PropertyName)).AppendLine(");");
                    builder.Append(indent).AppendLine("        }");
                }

                builder.Append(indent).AppendLine("        return __repoGenAssigned;");
                builder.Append(indent).AppendLine("    },");
            }

            builder.Append(indent).Append("    new global::System.Collections.Generic.Dictionary<string, object?> { ")
                .Append(string.Join(", ", (command.ExpectedVersions ?? []).Select(static version => "[" + System.Text.Json.JsonSerializer.Serialize(version.PropertyName) + "] = " + version.ValueSource)))
                .AppendLine(" },");
        }

        if (command.SavedResultKeys is not null) SavedCommandResultSourceRenderer.AppendProjection(builder, entity, command, indent);
        else builder.Append(indent).Append("    entity => ").Append(command.ResultSource ?? "0").AppendLine(",");
        builder.Append(indent).Append("    ").Append(command.SaveChanges ? "true" : "false").AppendLine(",");
        if (command.Kind != RepositoryCommandKind.TrackedDelete)
        {
            builder.Append(indent).Append("    ").Append(command.ReloadAfterSave ? "true" : "false").AppendLine(",");
        }

        builder.Append(indent).Append("    ").Append(work).AppendLine(",");
        builder.Append(indent).Append("    new string[] { ").Append(string.Join(", ", entity.CacheInvalidationTags.Select(static tag => "\"" + tag + "\"")))
            .Append(" }");
        CollectionReconciliationSourceRenderer.AppendArgument(builder, entity, command, indent);
        builder.AppendLine(", cancellationToken).ConfigureAwait(false);");
        if (returnResult)
        {
            builder.Append(indent).Append("return result").Append(command.ResultTypeName is null ? ".WithoutValue()" : "").AppendLine(";");
        }
    }
}
