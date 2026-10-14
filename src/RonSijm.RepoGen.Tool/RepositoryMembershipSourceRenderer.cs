using System.Text;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

/// <summary>Snapshots only request values; row filtering remains a provider-translated Contains expression.</summary>
internal static class RepositoryMembershipSourceRenderer
{
    internal static IEnumerable<string> Arguments(ConfiguredQueryModel query) =>
        (query.Memberships ?? []).SelectMany(static membership => new[] { membership.ValuesName, membership.ApplyName });

    internal static IEnumerable<(string Type, string Name)> Parameters(ConfiguredQueryModel query) =>
        (query.Memberships ?? []).SelectMany(static membership => new[]
            { (membership.ElementTypeName + "[]", membership.ValuesName), ("bool", membership.ApplyName) });

    internal static void AppendPreparation(StringBuilder builder, ConfiguredQueryModel query, string indent)
    {
        if (query.Memberships is not { Count: > 0 })
        {
            return;
        }

        void Line(string line) => builder.Append(indent).AppendLine(line);
        Line("cancellationToken.ThrowIfCancellationRequested();");
        foreach (var membership in query.Memberships)
        {
            var input = membership.LocalName + "Input";
            var items = membership.LocalName + "Items";
            var value = membership.LocalName + "Value";
            var seen = membership.LocalName + "Seen";
            var count = membership.LocalName + "Count";
            var parameter = System.Text.Json.JsonSerializer.Serialize(membership.ParameterName);
            Line("var " + input + " = " + membership.ValuesSource + ";");
            Line("if ((object?)" + input + " is global::System.Linq.IQueryable) throw new global::System.ArgumentException(\"Membership values must be in-memory, not IQueryable.\", " + parameter + ");");
            Line("var " + items + " = new global::System.Collections.Generic.List<" + membership.ElementTypeName + ">();");
            Line("var " + membership.ApplyName + " = true;");
            if (membership.Options.NullInput != RepositoryMembershipInputBehavior.EmptySet)
            {
                Line("if (" + input + " is null)");
                Line("{");
                Line("    " + (membership.Options.NullInput == RepositoryMembershipInputBehavior.Reject
                    ? "throw new global::System.ArgumentNullException(" + parameter + ", \"Membership values cannot be null.\");"
                    : membership.ApplyName + " = false;"));
                Line("}");
            }

            Line("if (" + input + " is not null)");
            Line("{");
            if (membership.Options.Distinct)
            {
                Line("    var " + seen + " = new global::System.Collections.Generic.HashSet<" + membership.ElementTypeName + ">();");
            }

            if (membership.Options.MaximumCount is not null)
            {
                Line("    var " + count + " = 0;");
            }

            Line("    foreach (var " + value + " in " + input + ")");
            Line("    {");
            Line("        cancellationToken.ThrowIfCancellationRequested();");
            if (membership.Options.MaximumCount is { } maximum)
            {
                // Check before incrementing: even int.MaxValue cannot overflow and dropped values still count.
                Line("        if (" + count + " == " + maximum.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                     ") throw new global::System.ArgumentOutOfRangeException(" + parameter + ", \"Membership input exceeds its maximum raw value count.\");");
                Line("        " + count + "++;");
            }

            if (membership.ElementTypeName.EndsWith('?') && membership.Options.NullValues != RepositoryMembershipNullValueBehavior.Include)
            {
                Line("        if (" + value + " is null) " + (membership.Options.NullValues == RepositoryMembershipNullValueBehavior.Ignore
                    ? "continue;"
                    : "throw new global::System.ArgumentException(\"Membership values cannot contain null.\", " + parameter + ");"));
            }

            if (membership.Options.Distinct)
            {
                Line("        if (!" + seen + ".Add(" + value + ")) continue;");
            }

            Line("        " + items + ".Add(" + value + ");");
            Line("    }");
            if (membership.Options.EmptyInput != RepositoryMembershipInputBehavior.EmptySet)
            {
                Line("    if (" + items + ".Count == 0) " + (membership.Options.EmptyInput == RepositoryMembershipInputBehavior.IgnoreFilter
                    ? membership.ApplyName + " = false;"
                    : "throw new global::System.ArgumentException(\"Membership values cannot be empty after normalization.\", " + parameter + ");"));
            }

            Line("}");
            Line("var " + membership.ValuesName + " = " + items + ".ToArray();");
        }

        Line("cancellationToken.ThrowIfCancellationRequested();");
    }
}