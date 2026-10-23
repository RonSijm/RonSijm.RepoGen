using System.Text;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static class CollectionReconciliationSourceRenderer
{
    internal static void AppendInputs(StringBuilder builder, ConfiguredCommandModel command, string indent)
    {
        if (command.Collections is null)
        {
            return;
        }

        builder.Append(indent).AppendLine("cancellationToken.ThrowIfCancellationRequested();");
        for (var index = 0; index < command.Collections.Count; index++)
        {
            var collection = command.Collections[index];
            builder.Append(indent).Append("var __repoGenCollection").Append(index).Append(" = ");
            if (collection.ConditionSource is not null)
            {
                builder.Append('(').Append(collection.ConditionSource).Append(") ? ");
            }

            builder.Append("global::RonSijm.RepoGen.EntityFrameworkCore.TrackedCollectionReconciler.PrepareInput<")
                .Append(collection.ItemTypeName).Append(", ").Append(collection.KeyTypeName).Append(">((")
                .Append(collection.ItemsSource).Append(") ?? throw new global::System.ArgumentNullException(\"items\"), item => ")
                .Append(collection.ItemKeySource).Append(", ").Append(collection.MaximumItems)
                .Append(collection.Link is null ? ")" : ", cancellationToken)");
            if (collection.ConditionSource is not null)
            {
                builder.Append(" : null");
            }

            builder.AppendLine(";");
        }
    }

    internal static void AppendArgument(StringBuilder builder, EntityRepositoryModel entity, ConfiguredCommandModel command, string indent)
    {
        if (command.Collections is null)
        {
            return;
        }

        builder.AppendLine(",");
        builder.Append(indent).Append("    new global::System.Func<").Append(entity.EntityTypeName)
            .AppendLine(", global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<global::RonSijm.RepoGen.EntityFrameworkCore.TrackedCollectionChanges>>[]");
        builder.Append(indent).AppendLine("    {");
        for (var index = 0; index < command.Collections.Count; index++)
        {
            var collection = command.Collections[index];
            builder.Append(indent).Append("        (entity, token) => global::RonSijm.RepoGen.EntityFrameworkCore.TrackedCollectionReconciler.")
                .Append(collection.Link is null ? "ApplyAsync<" : "ApplyLinksAsync<")
                .Append(entity.EntityTypeName).Append(", ").Append(collection.ChildTypeName).Append(", ").Append(collection.ItemTypeName).Append(", ").Append(collection.KeyTypeName).AppendLine(">(");
            builder.Append(indent).Append("            context, entity, \"").Append(collection.NavigationName).Append("\", entity => ")
                .Append(collection.NavigationSource).Append(", __repoGenCollection").Append(index).Append(", child => ").Append(collection.ChildKeySource).AppendLine(",");
            builder.Append(indent).Append("            ").Append(OptionalLinkReferenceSourceRenderer.CreateLambda(collection.CreateSource, collection.Link)).AppendLine(",");
            if (collection.Updates.Count == 0)
            {
                builder.Append(indent).AppendLine("            null,");
            }
            else
            {
                builder.Append(indent).AppendLine("            (child, item) =>");
                builder.Append(indent).AppendLine("            {");
                builder.Append(indent).AppendLine("                var assigned = new global::System.Collections.Generic.List<string>();");
                foreach (var update in collection.Updates)
                {
                    if (update.ConditionSource is not null)
                    {
                        builder.Append(indent).Append("                if (").Append(update.ConditionSource).AppendLine(")");
                    }

                    builder.Append(indent).AppendLine("                {");
                    if (update.MappedProperty is { } binding)
                        builder.Append(indent).Append("                    context.Entry(child).Property<").Append(binding.TypeName).Append(">(")
                            .Append(System.Text.Json.JsonSerializer.Serialize(binding.Name)).Append(").CurrentValue = ").Append(update.ValueBodySource).AppendLine(";");
                    else builder.Append(indent).Append("                    child.").Append(RepositoryModelFactory.EscapeIdentifier(update.PropertyName!))
                            .Append(" = ").Append(update.ValueBodySource).AppendLine(";");
                    builder.Append(indent).Append("                    assigned.Add(").Append(System.Text.Json.JsonSerializer.Serialize(update.PropertyName)).AppendLine(");");
                    builder.Append(indent).AppendLine("                }");
                }

                builder.Append(indent).AppendLine("                return assigned;");
                builder.Append(indent).AppendLine("            },");
            }

            builder.Append(indent).Append("            ").Append(collection.ExpectedVersions.Count == 0 ? "null" :
                "(child, item) => new global::System.Collections.Generic.Dictionary<string, object?> { " +
                string.Join(", ", collection.ExpectedVersions.Select(static version => "[" + System.Text.Json.JsonSerializer.Serialize(version.PropertyName) + "] = " + version.ValueSource)) + " }").AppendLine(",");
            builder.Append(indent).Append("            ").Append(collection.MissingItems == RepositoryCollectionRemovalBehavior.Delete ? "true" : "false").Append(", ")
                .Append(collection.MissingItems == RepositoryCollectionRemovalBehavior.Reject ? "true" : "false").Append(", query => ");
            var filter = "query";
            foreach (var predicate in collection.VisibilityPredicates)
            {
                filter = "global::System.Linq.Queryable.Where(" + filter + ", child => " + predicate + ")";
            }

            builder.Append(filter);
            if (collection.CreateSource is not null && SharedReferenceColumnSourceRenderer.HasCreatedOwnerGuard(collection.Link))
            {
                builder.Append(", matchesCreatedOwner: ").Append(SharedReferenceColumnSourceRenderer.CreatedOwnerPredicate(collection.Link!, fixture: false));
                if (collection.Link is { SharedReferenceColumns.Count: > 0 } sharedLink)
                    builder.Append(", matchesOwner: ").Append(SharedReferenceColumnSourceRenderer.Predicate(sharedLink, fixture: false));
                else builder.Append(", matchesOwner: null");
                builder.AppendLine(", cancellationToken: token),");
            }
            else if (collection.Link is { SharedReferenceColumns.Count: > 0 } link)
                builder.Append(", matchesOwner: ").Append(SharedReferenceColumnSourceRenderer.Predicate(link, fixture: false))
                    .AppendLine(", cancellationToken: token),");
            else builder.AppendLine(", token),");
        }

        builder.Append(indent).Append("    }");
    }
}
