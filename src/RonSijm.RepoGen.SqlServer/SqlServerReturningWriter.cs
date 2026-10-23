using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.SqlServer;

/// <summary>SQL Server native UPDATE/DELETE OUTPUT with EF-translated, parameterized predicates and setters.</summary>
[RepositoryProvider("Microsoft.EntityFrameworkCore.SqlServer")]
public sealed class SqlServerReturningWriter : RelationalReturningWriter
{
    protected override string ProviderName => "Microsoft.EntityFrameworkCore.SqlServer";

    protected override void ValidateProviderModel(IEntityType entityType)
    {
        if (entityType.IsMemoryOptimized())
        {
            throw new NotSupportedException("Returning SQL Server writes do not support memory-optimized tables.");
        }
    }

    protected override string CommandText(ISqlGenerationHelper helper, IEntityType entityType, string sourceSql,
        ReturningWriteKind kind, IReadOnlyList<IProperty> keys, IReadOnlyList<IProperty> updates, IReadOnlyList<ReturningWriteAssignment> assignments, IReadOnlyList<IProperty> returned)
    {
        var store = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        string Column(IProperty property) => helper.DelimitIdentifier(property.GetColumnName(store)!);
        var table = helper.DelimitIdentifier(entityType.GetTableName()!, entityType.GetSchema());
        // Lock the EF-translated target-table read before testing versions/computing new values.
        // This narrowly recognizes EF's quoted ordinary-table roots, not arbitrary caller SQL.
        // Fail closed if a provider version emits a different shape; never guess the table/alias.
        var roots = 0;
        sourceSql = Regex.Replace(sourceSql, @"\b(?:FROM|JOIN)\s+" + Regex.Escape(table) + @"\s+AS\s+\[(?:[^\]]|\]\])+\]",
            match =>
            {
                roots++;
                return match.Value + " WITH (UPDLOCK)";
            }, RegexOptions.CultureInvariant);
        if (roots == 0)
        {
            throw new NotSupportedException("The SQL Server returning source did not contain a recognized EF ordinary-table root.");
        }

        var target = helper.DelimitIdentifier("__repoGenTarget");
        var source = helper.DelimitIdentifier("__repoGenSource");
        var join = " FROM " + table + " AS " + target + " INNER JOIN (" + sourceSql + ") AS " + source + " ON " +
                   string.Join(" AND ", keys.Select((key, index) => target + "." + Column(key) + " = " + source + "." + helper.DelimitIdentifier(KeyAlias(index))));
        return kind == ReturningWriteKind.Delete
            ? "DELETE " + target + " OUTPUT " + string.Join(", ", returned.Select(property => "DELETED." + Column(property))) + join + ";"
            : "UPDATE " + target + " SET " + string.Join(", ", updates.Select((property, index) => Column(property) + " = " + source + "." + helper.DelimitIdentifier(assignments[index].ValueAlias))) +
              " OUTPUT " + string.Join(", ", returned.Select(property => "INSERTED." + Column(property))) + join + ";";
    }
}