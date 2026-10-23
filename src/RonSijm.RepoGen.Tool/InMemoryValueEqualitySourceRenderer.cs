using System.Text;

namespace RonSijm.RepoGen.Tool;

internal static class InMemoryValueEqualitySourceRenderer
{
    internal static string Assigned(IReadOnlyList<InMemoryValueEqualityModel> policies, IReadOnlyList<InMemoryPropertyModel>? bindings = null)
        => "static (__repoGenLeft, __repoGenRight, __repoGenProperty) => __repoGenProperty switch { " +
            string.Join(", ", policies.Select(policy => System.Text.Json.JsonSerializer.Serialize(policy.PropertyName) + " => " +
                policy.Compare("__repoGenLeft." + InMemoryWriteState.Access(policy.PropertyName, bindings), "__repoGenRight." + InMemoryWriteState.Access(policy.PropertyName, bindings)))) + ", _ => false }";

    internal static string Compare(IReadOnlyList<InMemoryValueEqualityModel>? policies, string property, string left, string right)
        => policies?.SingleOrDefault(policy => policy.PropertyName == property)?.Compare(left, right)
            ?? "global::RonSijm.RepoGen.Testing.InMemoryValueEquality.AreEqual(" + left + ", " + right + ")";

    internal static void Append(StringBuilder builder, IReadOnlyList<InMemoryValueEqualityModel> policies)
    {
        builder.AppendLine().Append("    internal static class ").Append(policies[0].HelperTypeName.Split('.').Last()).AppendLine("\n    {");
        foreach (var policy in policies)
            builder.Append("        internal static bool ").Append(policy.MethodName).Append('(').Append(policy.TypeName).Append(" left, ")
                .Append(policy.TypeName).Append(" right) => ").Append(policy.BodySource).AppendLine(";");
        builder.AppendLine("    }");
    }
}
