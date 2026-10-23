using System.Text;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static class BatchCommandSourceRenderer
{
    internal static void AppendInputs(StringBuilder builder, ConfiguredCommandModel command, string indent)
    {
        if (command.Batch is not { } batch)
        {
            return;
        }
        builder.Append(indent).AppendLine("cancellationToken.ThrowIfCancellationRequested();");
        builder.Append(indent).Append("var __repoGenBatchInput = global::RonSijm.RepoGen.EntityFrameworkCore.TrackedBatchCommandExecutor.PrepareInput<")
            .Append(batch.ItemTypeName).Append(", ").Append(batch.KeyTypeName).Append(">((").Append(batch.ItemsSource)
            .Append(") ?? throw new global::System.ArgumentNullException(\"items\"), item => ").Append(batch.ItemKeySource)
            .Append(", ").Append(batch.MaximumItems).AppendLine(", cancellationToken);");
        builder.Append(indent).AppendLine("if (__repoGenBatchInput.Items.Count == 0)");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    return new global::RonSijm.RepoGen.Abstractions.CommandResult")
            .Append(command.ResultValueTypeName is null ? "" : "<" + command.ResultValueTypeName + ">")
            .AppendLine("(global::RonSijm.RepoGen.Abstractions.CommandStatus.NoChanges);");
        builder.Append(indent).AppendLine("}");
    }

    internal static void AppendBody(StringBuilder builder, EntityRepositoryModel entity, ConfiguredCommandModel command,
        string indent, string work, bool returnResult, bool inputsPrepared)
    {
        var batch = command.Batch!;
        if (!inputsPrepared)
        {
            AppendInputs(builder, command, indent);
        }
        builder.Append(indent).Append("global::System.Linq.IQueryable<").Append(entity.EntityTypeName).Append("> query = context.Set<")
            .Append(entity.EntityTypeName).AppendLine(">();");
        foreach (var predicate in command.Predicates)
        {
            builder.Append(indent).Append("query = global::System.Linq.Queryable.Where(query, entity => ").Append(predicate.PredicateSource).AppendLine(");");
        }

        builder.Append(indent).Append("query = global::System.Linq.Queryable.Where(query, entity => global::System.Linq.Enumerable.Contains(__repoGenBatchInput.Keys, ")
            .Append(batch.EntityKeySource).AppendLine("));");
        builder.Append(indent).Append("var result = await global::RonSijm.RepoGen.EntityFrameworkCore.TrackedBatchCommandExecutor.ExecuteAsync<")
            .Append(entity.EntityTypeName).Append(", ").Append(batch.ItemTypeName).Append(", ").Append(batch.KeyTypeName).Append(", ")
            .Append(command.ResultTypeName ?? "int").AppendLine(">(");
        builder.Append(indent).Append("    context, query, __repoGenBatchInput, entity => ").Append(batch.EntityKeySource).AppendLine(",");
        if (command.Kind == RepositoryCommandKind.TrackedDelete)
        {
            builder.Append(indent).AppendLine("    null,");
        }
        else
        {
            builder.Append(indent).AppendLine("    (entity, item) =>");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        var __repoGenBatchAssigned = new global::System.Collections.Generic.List<string>();");
            foreach (var update in batch.Updates)
            {
                if (update.ConditionSource is not null)
                {
                    builder.Append(indent).Append("        if (").Append(update.ConditionSource).AppendLine(")");
                }
                builder.Append(indent).AppendLine("        {");
                if (update.MappedProperty is { } binding)
                    builder.Append(indent).Append("            context.Entry(entity).Property<").Append(binding.TypeName).Append(">(")
                        .Append(System.Text.Json.JsonSerializer.Serialize(binding.Name)).Append(").CurrentValue = ").Append(update.ValueBodySource).AppendLine(";");
                else
                    builder.Append(indent).Append("            entity.").Append(RepositoryModelFactory.EscapeIdentifier(update.PropertyName!)).Append(" = ")
                        .Append(update.ValueBodySource).AppendLine(";");
                builder.Append(indent).Append("            __repoGenBatchAssigned.Add(")
                    .Append(System.Text.Json.JsonSerializer.Serialize(update.PropertyName)).AppendLine(");");
                builder.Append(indent).AppendLine("        }");
            }

            builder.Append(indent).AppendLine("        return __repoGenBatchAssigned;");
            builder.Append(indent).AppendLine("    },");
        }

        builder.Append(indent).Append("    ").Append(batch.ExpectedVersions.Count == 0 ? "null" :
            "(entity, item) => new global::System.Collections.Generic.Dictionary<string, object?> { " +
            string.Join(", ", batch.ExpectedVersions.Select(static version => "[" + System.Text.Json.JsonSerializer.Serialize(version.PropertyName) + "] = " + version.ValueSource)) + " }").AppendLine(",");
        if (command.SavedResultKeys is not null) SavedBatchResultSourceRenderer.AppendProjection(builder, entity, command, indent);
        else builder.Append(indent).Append("    entity => ").Append(command.ResultSource ?? "0").AppendLine(",");
        builder.Append(indent).Append("    ").Append(command.Kind == RepositoryCommandKind.TrackedDelete ? "true" : "false").Append(", ")
            .Append(command.SaveChanges ? "true" : "false").Append(", ").Append(work).AppendLine(",");
        builder.Append(indent).Append("    new string[] { ").Append(string.Join(", ", entity.CacheInvalidationTags.Select(static tag => "\"" + tag + "\"")))
            .AppendLine(" }, cancellationToken).ConfigureAwait(false);");
        if (returnResult)
        {
            builder.Append(indent).Append("return result").Append(command.ResultTypeName is null ? ".WithoutValue()" : "").AppendLine(";");
        }
    }
}
