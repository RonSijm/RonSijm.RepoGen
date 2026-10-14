using System.Text;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static class AtomicCommandSourceRenderer
{
    public static void AppendBody(StringBuilder builder, EntityRepositoryModel entity, ConfiguredCommandModel command, RepositoryGenerationSettingsModel settings)
    {
        const string indent = "            ";
        void Line(string value) => builder.Append(indent).AppendLine(value);
        Line("var result = await global::RonSijm.RepoGen.EntityFrameworkCore.AtomicWriteExecutor.ExecuteAsync<" + entity.EntityTypeName + ", " + (command.ResultTypeName ?? "int") + ">(");
        Line("    context, __repoGenAtomicWriter, () => " + command.CreateSource + ",");
        Line("    new string[] { " + string.Join(", ", (command.InsertColumns ?? []).Select(static name => "\"" + name + "\"")) + " },");
        Line("    new string[] { " + string.Join(", ", (command.ConflictColumns ?? []).Select(static name => "\"" + name + "\"")) + " },");
        if (command.Kind == RepositoryCommandKind.InsertIfAbsent)
        {
            Line("    null,");
        }
        else
        {
            Line("    async (__repoGenCandidate, __repoGenToken) =>");
            Line("    {");
            Line("        global::System.Linq.IQueryable<" + entity.EntityTypeName + "> query = context.Set<" + entity.EntityTypeName + ">();");
            foreach (var name in command.ConflictColumns ?? [])
            {
                var property = RepositoryModelFactory.EscapeIdentifier(name);
                Line("        query = global::System.Linq.Queryable.Where(query, entity => entity." + property + " == __repoGenCandidate." + property + ");");
            }

            foreach (var predicate in command.Predicates)
            {
                Line("        query = global::System.Linq.Queryable.Where(query, entity => " + predicate.PredicateSource + ");");
            }

            Line("        var __repoGenAffected = await global::Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.ExecuteUpdateAsync(query, setters => setters");
            foreach (var update in command.Updates)
            {
                Line("            .SetProperty(" + update.PropertyExpressionSource + ", " + update.ValueExpressionSource + ")");
            }

            Line("            , __repoGenToken).ConfigureAwait(false);");
            Line("        if (__repoGenAffected == 0) return null;");
            // Conditional predicates may no longer match after applying the update (e.g. a version increment). Reload by the immutable conflict key only.
            var root = "context.Set<" + entity.EntityTypeName + ">()";
            foreach (var name in command.ConflictColumns ?? [])
            {
                var property = RepositoryModelFactory.EscapeIdentifier(name);
                root = "global::System.Linq.Queryable.Where(" + root + ", entity => entity." + property + " == __repoGenCandidate." + property + ")";
            }

            Line("        return await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(" + root + "), __repoGenToken).ConfigureAwait(false);");
            Line("    },");
        }

        Line("    entity => " + (command.ResultSource ?? "0") + ",");
        Line("    (object)contextSource as global::RonSijm.RepoGen.Abstractions.IRepositoryUnitOfWork,");
        Line("    new string[] { " + string.Join(", ", entity.CacheInvalidationTags.Select(static tag => "\"" + tag + "\"")) + " }, cancellationToken).ConfigureAwait(false);");
        if (settings.ClosedCaching)
        {
            Line("if (result.Succeeded && (object)contextSource is not global::RonSijm.RepoGen.Abstractions.IRepositoryUnitOfWork)");
            Line("    await global::RonSijm.RepoGen.EntityFrameworkCore.RepositoryCacheCoordinator.WrittenAsync(cache, contextSource, context, __repoGenScope, __repoGenWriteTags, cancellationToken).ConfigureAwait(false);");
        }

        Line("return result" + (command.ResultTypeName is null ? ".WithoutValue()" : "") + ";");
    }
}