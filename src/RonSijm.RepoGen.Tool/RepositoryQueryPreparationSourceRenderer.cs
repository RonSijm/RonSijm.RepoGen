using System.Text;
using System.Text.Json;

namespace RonSijm.RepoGen.Tool;

/// <summary>Snapshots and validates request-only inputs before acquisition, cache lookup and repeated query construction.</summary>
internal static class RepositoryQueryPreparationSourceRenderer
{
    private static IEnumerable<ConfiguredFieldSortModel> FieldSorts(ConfiguredQueryModel query) =>
        (query.Pipeline?.Stages ?? []).Select(static stage => stage.FieldSort).OfType<ConfiguredFieldSortModel>();

    internal static bool Required(ConfiguredQueryModel query) => query.Memberships is { Count: > 0 } || FieldSorts(query).Any() ||
                                                                 query.Cursor?.RequestParameterName is not null || query.CursorValidations is { Count: > 0 };

    internal static IEnumerable<string> Arguments(ConfiguredQueryModel query) =>
        RepositoryMembershipSourceRenderer.Arguments(query).Concat(FieldSorts(query).SelectMany(static sort => new[] { sort.IndexName, sort.DescendingName }));

    internal static IEnumerable<(string Type, string Name)> Parameters(ConfiguredQueryModel query) =>
        RepositoryMembershipSourceRenderer.Parameters(query).Concat(FieldSorts(query).SelectMany(static sort => new[] { ("int", sort.IndexName), ("bool", sort.DescendingName) }));

    internal static void AppendPreparation(StringBuilder builder, ConfiguredQueryModel query, string indent)
    {
        if (query.Cursor?.RequestParameterName is not null || query.CursorValidations is { Count: > 0 })
        {
            builder.Append(indent).AppendLine("cancellationToken.ThrowIfCancellationRequested();");
            builder.Append(indent).AppendLine("global::System.ArgumentNullException.ThrowIfNull(cursorPage);");
            foreach (var validation in query.CursorValidations ?? [])
            {
                builder.Append(indent).Append("if (cursorPage.HasCursor && !(").Append(validation.PredicateSource).AppendLine("))");
                builder.Append(indent).Append("    throw new global::System.ArgumentException(").Append(validation.MessageSource).AppendLine(", nameof(cursorPage));");
                builder.Append(indent).AppendLine("cancellationToken.ThrowIfCancellationRequested();");
            }
        }

        RepositoryMembershipSourceRenderer.AppendPreparation(builder, query, indent);
        var sorts = FieldSorts(query).ToArray();
        if (sorts.Length == 0)
        {
            return;
        }

        void Line(string line) => builder.Append(indent).AppendLine(line);
        Line("cancellationToken.ThrowIfCancellationRequested();");
        foreach (var sort in sorts)
        {
            var input = sort.LocalName + "Input";
            var parameter = JsonSerializer.Serialize(sort.ParameterName);
            Line("var " + input + " = " + sort.FieldNameSource + ";");
            Line("cancellationToken.ThrowIfCancellationRequested();");
            Line("var " + sort.IndexName + " = -1;");
            Line("var " + sort.DescendingName + " = false;");
            if (!sort.PreserveOrderWhenUnspecified)
            {
                Line("if (global::System.String.IsNullOrEmpty(" + input + "))");
                Line("{");
                if (sort.DefaultField is { } fallback)
                {
                    var defaultIndex = sort.Fields.ToList().FindIndex(field => field.Name == fallback);
                    Line("    " + sort.IndexName + " = " + defaultIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";");
                    Line("    " + sort.DescendingName + " = " + (sort.DefaultDescending ? "true" : "false") + ";");
                }
                else
                {
                    Line("    throw new global::System.ArgumentException(\"A whitelisted sort field is required.\", " + parameter + ");");
                }

                Line("}");
                Line("else");
            }
            else
            {
                Line("if (!global::System.String.IsNullOrEmpty(" + input + "))");
            }

            Line("{");
            for (var index = 0; index < sort.Fields.Count; index++)
            {
                Line("    " + (index == 0 ? "if" : "else if") + " (global::System.String.Equals(" + input + ", " + JsonSerializer.Serialize(sort.Fields[index].Name) +
                     ", global::System.StringComparison.OrdinalIgnoreCase)) " + sort.IndexName + " = " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";");
            }

            Line("    else throw new global::System.ArgumentException(\"The sort field is not in this operation's whitelist.\", " + parameter + ");");
            Line("    " + sort.DescendingName + " = " + sort.DescendingSource + ";");
            Line("}");
            Line("cancellationToken.ThrowIfCancellationRequested();");
        }
    }
}