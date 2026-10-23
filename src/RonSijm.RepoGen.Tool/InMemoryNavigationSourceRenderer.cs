using System.Text.Json;

namespace RonSijm.RepoGen.Tool;

internal static class InMemoryNavigationSourceRenderer
{
    internal static void AppendSnapshot(Action<string> line, string data, InMemoryNavigationModel navigation, string? suppliedSnapshot = null)
    {
        var filteredPrincipals = navigation.Relationships.Where(static edge => edge.Join is not null)
            .SelectMany(static edge => new[] { edge.SourceVariableName, edge.TargetVariableName })
            .Where(name => navigation.Roots.Any(root => root.VariableName == name && root.ModelQueryFilter is not null))
            .ToHashSet(StringComparer.Ordinal);
        line("var __repoGenSnapshot = " + (suppliedSnapshot ?? data + ".CaptureSnapshot(" + string.Join(", ", navigation.Roots.Select(root => JsonSerializer.Serialize(root.EntityTypeName))) + ")") + ";");
        foreach (var root in navigation.Roots)
        {
            var rows = "__repoGenSnapshot.GetRows<" + (root.StoreRowTypeName ?? root.EntityTypeName) + ">(" + JsonSerializer.Serialize(root.EntityTypeName) + ")";
            if (root.EntityMapping is { } mapping)
                rows = "global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(global::RonSijm.RepoGen.Testing.InMemoryQueryExecution.WithCancellation(" + rows +
                    ", cancellationToken), " + mapping.ExpressionSource + "))";
            if (root.ModelQueryFilter is { } filter)
            {
                if (filteredPrincipals.Contains(root.VariableName))
                {
                    line("var " + Unfiltered(root.VariableName) + " = " + rows + ";");
                    rows = Unfiltered(root.VariableName);
                }
                rows = "global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(global::RonSijm.RepoGen.Testing.InMemoryQueryExecution.WithCancellation(" +
                    rows + ", cancellationToken), " + filter.PredicateSource + "))";
            }
            line("var " + root.VariableName + " = " + rows + ";");
        }
        foreach (var edge in navigation.Relationships)
        {
            if (edge.Join is { } join)
            {
                var filtered = filteredPrincipals.Contains(edge.SourceVariableName) || filteredPrincipals.Contains(edge.TargetVariableName);
                line("global::RonSijm.RepoGen.Testing.InMemoryRelationships." + (filtered ? "LoadFilteredManyToMany(" : "LoadManyToMany(") +
                    JsonSerializer.Serialize(edge.Name) + ", " + edge.SourceVariableName + ", " + edge.TargetVariableName + ", " + join.VariableName + ", " +
                    (filtered ? (filteredPrincipals.Contains(edge.SourceVariableName) ? Unfiltered(edge.SourceVariableName) : edge.SourceVariableName) + ", " +
                        (filteredPrincipals.Contains(edge.TargetVariableName) ? Unfiltered(edge.TargetVariableName) : edge.TargetVariableName) + ", " : "") +
                    edge.SourceKeySource + ", " + edge.TargetKeySource + ", " + join.SourceKeySource + ", " + join.TargetKeySource + ", " + edge.AssignmentSource + ", " +
                    "requiredSource: " + (join.RequiredSource ? "true" : "false") + ", requiredTarget: " + (join.RequiredTarget ? "true" : "false") +
                    (join.PreserveMissingTargets ? ", preserveMissingTargets: true" : "") +
                    (join.SourceKeyComparerSource is null && join.TargetKeyComparerSource is null ? "" :
                        ", sourceKeyComparer: " + (join.SourceKeyComparerSource ?? "null") + ", targetKeyComparer: " + (join.TargetKeyComparerSource ?? "null")) + ", cancellationToken: cancellationToken);");
            }
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

    private static string Unfiltered(string variable) => variable + "Unfiltered";
}
