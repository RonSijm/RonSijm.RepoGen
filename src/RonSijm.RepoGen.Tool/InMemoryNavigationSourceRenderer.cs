using System.Text.Json;

namespace RonSijm.RepoGen.Tool;

internal static class InMemoryNavigationSourceRenderer
{
    internal static void AppendSnapshot(Action<string> line, string data, InMemoryNavigationModel navigation)
    {
        line("var __repoGenSnapshot = " + data + ".CaptureSnapshot(" + string.Join(", ", navigation.Roots.Select(root => JsonSerializer.Serialize(root.EntityTypeName))) + ");");
        foreach (var root in navigation.Roots)
        {
            var rows = "__repoGenSnapshot.GetRows<" + (root.StoreRowTypeName ?? root.EntityTypeName) + ">(" + JsonSerializer.Serialize(root.EntityTypeName) + ")";
            if (root.EntityMapping is { } mapping)
                rows = "global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(global::RonSijm.RepoGen.Testing.InMemoryQueryExecution.WithCancellation(" + rows +
                    ", cancellationToken), " + mapping.ExpressionSource + "))";
            if (root.ModelQueryFilter is { } filter)
                rows = "global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(global::RonSijm.RepoGen.Testing.InMemoryQueryExecution.WithCancellation(" +
                    rows + ", cancellationToken), " + filter.PredicateSource + "))";
            line("var " + root.VariableName + " = " + rows + ";");
        }
        foreach (var edge in navigation.Relationships)
        {
            if (edge.Join is { } join)
                line("global::RonSijm.RepoGen.Testing.InMemoryRelationships.LoadManyToMany(" +
                    JsonSerializer.Serialize(edge.Name) + ", " + edge.SourceVariableName + ", " + edge.TargetVariableName + ", " + join.VariableName + ", " +
                    edge.SourceKeySource + ", " + edge.TargetKeySource + ", " + join.SourceKeySource + ", " + join.TargetKeySource + ", " + edge.AssignmentSource + ", " +
                    "requiredSource: " + (join.RequiredSource ? "true" : "false") + ", requiredTarget: " + (join.RequiredTarget ? "true" : "false") +
                    (join.PreserveMissingTargets ? ", preserveMissingTargets: true" : "") +
                    (join.SourceKeyComparerSource is null && join.TargetKeyComparerSource is null ? "" :
                        ", sourceKeyComparer: " + (join.SourceKeyComparerSource ?? "null") + ", targetKeyComparer: " + (join.TargetKeyComparerSource ?? "null")) + ", cancellationToken: cancellationToken);");
            else
                line("global::RonSijm.RepoGen.Testing.InMemoryRelationships." + (edge.IsCollection ? "LoadCollection" : "LoadReference") + "(" +
                    JsonSerializer.Serialize(edge.Name) + ", " + edge.SourceVariableName + ", " + edge.TargetVariableName + ", " +
                    edge.SourceKeySource + ", " + edge.TargetKeySource + ", " + edge.AssignmentSource + ", " +
                    (edge.IsCollection ? "" : "required: " + (edge.Required ? "true" : "false") + ", ") +
                    (edge.KeyComparerSource is null ? "" : "keyComparer: " + edge.KeyComparerSource + ", ") + "cancellationToken: cancellationToken);");
        }
        line("cancellationToken.ThrowIfCancellationRequested();");
    }

    internal static string Rows(InMemoryNavigationModel navigation, string entityTypeName) =>
        navigation.Roots.Single(root => root.EntityTypeName == entityTypeName).VariableName;
}
