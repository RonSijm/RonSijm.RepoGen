using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.Sqlite;

/// <summary>SQLite 3.35+ native UPDATE FROM / DELETE RETURNING. No fetch/save fallback.</summary>
[RepositoryProvider("Microsoft.EntityFrameworkCore.Sqlite")]
public sealed class SqliteReturningWriter : RelationalReturningWriter
{
    protected override string ProviderName => "Microsoft.EntityFrameworkCore.Sqlite";

    protected override void ValidateProviderModel(IEntityType entityType)
    {
        if (entityType.GetSchema() is not null)
        {
            throw new NotSupportedException("SQLite returning writes do not support schemas.");
        }
    }

    protected override string CommandText(ISqlGenerationHelper helper, IEntityType entityType, string sourceSql,
        ReturningWriteKind kind, IReadOnlyList<IProperty> keys, IReadOnlyList<IProperty> updates, IReadOnlyList<ReturningWriteAssignment> assignments, IReadOnlyList<IProperty> returned)
    {
        var store = StoreObjectIdentifier.Table(entityType.GetTableName()!, null);
        string Column(IProperty property) => helper.DelimitIdentifier(property.GetColumnName(store)!);
        var table = helper.DelimitIdentifier(entityType.GetTableName()!);
        var target = helper.DelimitIdentifier("__repoGenTarget");
        var source = helper.DelimitIdentifier("__repoGenSource");
        var match = string.Join(" AND ", keys.Select((key, index) => target + "." + Column(key) + " = " + source + "." + helper.DelimitIdentifier(KeyAlias(index))));
        var output = " RETURNING " + string.Join(", ", returned.Select(Column)) + ";";
        return kind == ReturningWriteKind.Delete
            ? "DELETE FROM " + table + " AS " + target + " WHERE EXISTS (SELECT 1 FROM (" + sourceSql + ") AS " + source + " WHERE " + match + ")" + output
            : "UPDATE " + table + " AS " + target + " SET " + string.Join(", ", updates.Select((property, index) => Column(property) + " = " + source + "." + helper.DelimitIdentifier(assignments[index].ValueAlias))) +
              " FROM (" + sourceSql + ") AS " + source + " WHERE " + match + output;
    }
}