using System.Text;

namespace RonSijm.RepoGen.Tool;

/// <summary>Reads a DTO from saved tables without reusing mutation predicates or tracked relationship graphs.</summary>
internal static class SavedCommandResultSourceRenderer
{
    internal static void AppendProjection(StringBuilder builder, EntityRepositoryModel entity, ConfiguredCommandModel command, string indent, bool capturedState = false)
    {
        void Line(string value) => builder.Append(indent).AppendLine(value);
        Line("    async (" + (capturedState ? "__repoGenReturnedRow" : "__repoGenSavedEntity") + ", __repoGenResultToken) =>");
        Line("    {");
        if (capturedState)
        {
            Line("        var __repoGenSavedEntity = __repoGenReturnedRow.Entity;");
        }
        Line("        var __repoGenSavedQuery = global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(context.Set<" + entity.EntityTypeName + ">());");
        Line("        __repoGenSavedQuery = global::System.Linq.Queryable.Where(__repoGenSavedQuery, entity => " +
            string.Join(" && ", command.SavedResultKeys!.Select(key => "global::System.Object.Equals(entity." + RepositoryModelFactory.EscapeIdentifier(key) +
                ", __repoGenSavedEntity." + RepositoryModelFactory.EscapeIdentifier(key) + ")")) + ");");
        foreach (var policy in (entity.VisibilityPolicies ?? []).Where(policy => command.BypassedVisibility?.Contains(policy.Name, StringComparer.Ordinal) != true))
            Line("        __repoGenSavedQuery = global::System.Linq.Queryable.Where(__repoGenSavedQuery, entity => " + policy.PredicateSource + ");");
        Line("        return await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(");
        Line("            global::System.Linq.Queryable.Select(__repoGenSavedQuery, entity => " + command.ResultSource + "), __repoGenResultToken).ConfigureAwait(false);");
        Line("    },");
    }
}
