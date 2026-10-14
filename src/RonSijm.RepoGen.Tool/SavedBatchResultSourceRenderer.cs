using System.Text;

namespace RonSijm.RepoGen.Tool;

internal static class SavedBatchResultSourceRenderer
{
    internal static void AppendProjection(StringBuilder builder, EntityRepositoryModel entity, ConfiguredCommandModel command, string indent)
    {
        void Line(string value) => builder.Append(indent).AppendLine(value);
        var keys = command.SavedResultKeys!;
        Line("    async (__repoGenSavedEntities, __repoGenResultToken) =>");
        Line("    {");
        Line("        var __repoGenSavedQuery = global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(context.Set<" + entity.EntityTypeName + ">());");
        Line("        __repoGenSavedQuery = global::RonSijm.RepoGen.EntityFrameworkCore.SavedBatchQuery.Restrict(__repoGenSavedQuery, __repoGenSavedEntities, (entity, saved) => " +
            string.Join(" && ", keys.Select(key => "global::System.Object.Equals(entity." + RepositoryModelFactory.EscapeIdentifier(key) + ", saved." + RepositoryModelFactory.EscapeIdentifier(key) + ")")) + ");");
        foreach (var policy in (entity.VisibilityPolicies ?? []).Where(policy => command.BypassedVisibility?.Contains(policy.Name, StringComparer.Ordinal) != true))
            Line("        __repoGenSavedQuery = global::System.Linq.Queryable.Where(__repoGenSavedQuery, entity => " + policy.PredicateSource + ");");
        Line("        var __repoGenProjectedRows = await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(");
        Line("            global::System.Linq.Queryable.Select(__repoGenSavedQuery, entity => new { " +
            string.Join(", ", keys.Select((key, index) => "Key" + index + " = entity." + RepositoryModelFactory.EscapeIdentifier(key))) +
            ", Value = " + command.ResultSource + " }), __repoGenResultToken).ConfigureAwait(false);");
        Line("        return global::RonSijm.RepoGen.EntityFrameworkCore.SavedBatchQuery.InInputOrder(__repoGenSavedEntities, __repoGenProjectedRows,");
        Line("            entity => new global::System.Object?[] { " + string.Join(", ", keys.Select(key => "entity." + RepositoryModelFactory.EscapeIdentifier(key))) + " },");
        Line("            row => new global::System.Object?[] { " + string.Join(", ", keys.Select((_, index) => "row.Key" + index)) + " }, row => row.Value, __repoGenResultToken);");
        Line("    },");
    }
}
