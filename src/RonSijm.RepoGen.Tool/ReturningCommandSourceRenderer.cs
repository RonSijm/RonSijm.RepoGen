using System.Text;

namespace RonSijm.RepoGen.Tool;

internal static class ReturningCommandSourceRenderer
{
    public static void AppendBody(StringBuilder builder, EntityRepositoryModel entity, ConfiguredCommandModel command, RepositoryGenerationSettingsModel settings)
    {
        void Line(string value) => builder.Append("            ").AppendLine(value);
        Line("global::System.Linq.IQueryable<" + entity.EntityTypeName + "> __repoGenQuery = context.Set<" + entity.EntityTypeName + ">();");
        foreach (var predicate in command.Predicates)
        {
            if (predicate.ConditionSource is not null)
            {
                Line("if (" + predicate.ConditionSource + ")");
            }

            Line("    __repoGenQuery = global::System.Linq.Queryable.Where(__repoGenQuery, entity => " + predicate.PredicateSource + ");");
        }

        Line("var __repoGenSource = global::System.Linq.Queryable.Select(__repoGenQuery, entity => new");
        Line("{");
        foreach (var (name, index) in (command.ReturningKeys ?? []).Select(static (name, index) => (name, index)))
        {
            Line("    __repoGenKey" + index + " = entity." + RepositoryModelFactory.EscapeIdentifier(name) + ",");
        }

        foreach (var update in command.Updates.DistinctBy(static update => update.NativeValueAlias).Where(static update => update.NativeValueAlias!.StartsWith("__repoGenValue", StringComparison.Ordinal)))
        {
            Line("    " + update.NativeValueAlias + " = (" + update.NativeValueTypeName + ")(" + update.ValueBodySource + "),");
        }

        Line("});");
        var fresh = command.SavedResultMaximumRows is not null;
        var executor = fresh
            ? command.ReturningState is null ? "ExecuteUpdateWithResultQueryAsync" : command.ReturningState.IncludeMappedProperties ? "ExecuteUpdateWithMappedStateResultQueryAsync" : "ExecuteUpdateWithStateResultQueryAsync"
            : command.ReturningState is null ? "ExecuteAsync" : command.ReturningState.IncludeMappedProperties ? "ExecuteWithMappedStateAsync" : "ExecuteWithStateAsync";
        Line("var result = await global::RonSijm.RepoGen.EntityFrameworkCore.ReturningWriteExecutor." + executor + "<" + entity.EntityTypeName + ", " + command.ResultTypeName + ">(");
        Line("    context, __repoGenReturningWriter, __repoGenSource," + (fresh ? "" : " global::RonSijm.RepoGen.EntityFrameworkCore.ReturningWriteKind." + command.Kind + ","));
        Line("    new global::RonSijm.RepoGen.EntityFrameworkCore.ReturningWriteAssignment[] { " + string.Join(", ", command.Updates.Select(static update =>
            "new(\"" + update.PropertyName + "\", \"" + update.NativeValueAlias + "\")")) + " },");
        if (fresh)
        {
            Line("    " + command.SavedResultMaximumRows + ",");
            SavedBatchResultSourceRenderer.AppendProjection(builder, entity, command, "            ", capturedState: command.ReturningState is not null);
            Line("    (object)contextSource as global::RonSijm.RepoGen.Abstractions.IRepositoryUnitOfWork,");
        }
        else
        {
            Line("    " + (command.ReturningState is null ? "entity => " + command.ResultSource :
                "__repoGenReturnedRow => { var entity = __repoGenReturnedRow.Entity; return " + command.ResultSource + "; }") +
                ", (object)contextSource as global::RonSijm.RepoGen.Abstractions.IRepositoryUnitOfWork,");
        }
        Line("    new string[] { " + string.Join(", ", entity.CacheInvalidationTags.Select(static tag => "\"" + tag + "\"")) + " }, cancellationToken).ConfigureAwait(false);");
        if (settings.ClosedCaching)
        {
            Line("if (result.AffectedRows > 0 && (object)contextSource is not global::RonSijm.RepoGen.Abstractions.IRepositoryUnitOfWork)");
            Line("    await global::RonSijm.RepoGen.EntityFrameworkCore.RepositoryCacheCoordinator.WrittenAsync(cache, contextSource, context, __repoGenScope, __repoGenWriteTags, global::System.Threading.CancellationToken.None).ConfigureAwait(false);");
        }

        Line("return result;");
    }
}
