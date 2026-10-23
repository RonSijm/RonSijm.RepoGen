using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace RonSijm.RepoGen.AdventureWorks.Scaffolding;

/// <summary>Reads this sample's DDL into EF's database metadata without executing the install script.</summary>
internal sealed partial class SchemaReader : TSqlFragmentVisitor
{
    private readonly DatabaseModel database = new() { DatabaseName = "AdventureWorks", DefaultSchema = "dbo" };
    private readonly Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sysname"] = "nvarchar(128)"
    };
    private readonly Sql160ScriptGenerator generator = new();
    private readonly List<AlterTableAddTableElementStatement> alterations = [];
    private readonly List<CreateIndexStatement> indexes = [];

    // SQL Server normally reports computed-column result types through sys.columns.
    // These are the ten expressions in the pinned AdventureWorks OLTP schema.
    private static readonly Dictionary<string, (string StoreType, bool Nullable)> ComputedTypes = new(StringComparer.Ordinal)
    {
        ["Sales.Customer.AccountNumber"] = ("varchar(10)", false),
        ["Production.Document.DocumentLevel"] = ("smallint", true),
        ["HumanResources.Employee.OrganizationLevel"] = ("smallint", true),
        ["Purchasing.PurchaseOrderDetail.LineTotal"] = ("money", false),
        ["Purchasing.PurchaseOrderDetail.StockedQty"] = ("decimal(9,2)", false),
        ["Purchasing.PurchaseOrderHeader.TotalDue"] = ("money", false),
        ["Sales.SalesOrderDetail.LineTotal"] = ("numeric(38,6)", false),
        ["Sales.SalesOrderHeader.SalesOrderNumber"] = ("nvarchar(25)", false),
        ["Sales.SalesOrderHeader.TotalDue"] = ("money", false),
        ["Production.WorkOrder.StockedQty"] = ("int", false)
    };

    internal static DatabaseModel Read(string path)
    {
        var reader = new SchemaReader();
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        // The other batches install databases, import CSVs, and create procedures/triggers/views.
        // Only table/type/index DDL is needed for the repository model.
        foreach (var batch in BatchSeparator().Split(File.ReadAllText(path)))
        {
            if (!SchemaStatement().IsMatch(batch))
            {
                continue;
            }

            using var text = new StringReader(batch);
            var fragment = parser.Parse(text, out var errors);
            if (errors.Count != 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine,
                    errors.Select(error => $"SQL {error.Number} at line {error.Line}: {error.Message}")));
            }

            fragment.Accept(reader);
        }

        foreach (var statement in reader.alterations)
        {
            reader.AddConstraints(reader.Table(statement.SchemaObjectName), statement.Definition.TableConstraints);
        }

        foreach (var statement in reader.indexes)
        {
            var table = reader.database.Tables.SingleOrDefault(table =>
                table.Schema == statement.OnName.SchemaIdentifier.Value && table.Name == statement.OnName.BaseIdentifier.Value);
            if (table is null)
            {
                continue; // The install script also indexes views, which are outside this table demo.
            }

            var index = new DatabaseIndex
            {
                Table = table,
                Name = statement.Name.Value,
                IsUnique = statement.Unique,
                Filter = statement.FilterPredicate is null ? null : reader.Sql(statement.FilterPredicate)
            };
            foreach (var column in statement.Columns)
            {
                index.Columns.Add(Column(table, column.Column.MultiPartIdentifier.Identifiers[^1].Value));
                index.IsDescending.Add(column.SortOrder == SortOrder.Descending);
            }

            if (statement.Clustered.HasValue)
            {
                index["SqlServer:Clustered"] = statement.Clustered.Value;
            }

            if (statement.IncludeColumns.Count != 0)
            {
                index["SqlServer:Include"] = statement.IncludeColumns
                    .Select(column => column.MultiPartIdentifier.Identifiers[^1].Value).ToArray();
            }

            table.Indexes.Add(index);
        }

        if (reader.database.Tables.Count != 71 || reader.database.Tables.Any(table => table.PrimaryKey is null))
        {
            throw new InvalidOperationException("Expected the pinned OLTP schema's 71 tables and primary keys. Review the upstream schema before regenerating.");
        }

        return reader.database;
    }

    public override void ExplicitVisit(CreateTypeUddtStatement node) =>
        aliases.Add(node.Name.BaseIdentifier.Value, StoreType(node.DataType));

    public override void ExplicitVisit(AlterTableAddTableElementStatement node) => alterations.Add(node);

    public override void ExplicitVisit(CreateIndexStatement node) => indexes.Add(node);

    public override void ExplicitVisit(CreateTableStatement node)
    {
        var table = new DatabaseTable
        {
            Database = database,
            Name = node.SchemaObjectName.BaseIdentifier.Value,
            Schema = node.SchemaObjectName.SchemaIdentifier.Value
        };
        database.Tables.Add(table);
        foreach (var definition in node.Definition.ColumnDefinitions)
        {
            var column = new DatabaseColumn
            {
                Table = table,
                Name = definition.ColumnIdentifier.Value,
                IsNullable = definition.Constraints.OfType<NullableConstraintDefinition>().SingleOrDefault()?.Nullable ?? true,
                DefaultValueSql = definition.DefaultConstraint is null ? null : Sql(definition.DefaultConstraint.Expression)
            };
            if (definition.ComputedColumnExpression is not null)
            {
                var result = ComputedTypes[$"{table.Schema}.{table.Name}.{column.Name}"];
                column.StoreType = result.StoreType;
                column.IsNullable = result.Nullable;
                column.ComputedColumnSql = Sql(definition.ComputedColumnExpression);
                column.IsStored = definition.IsPersisted;
                column.ValueGenerated = ValueGenerated.OnAddOrUpdate;
            }
            else if (definition.DataType is XmlDataTypeReference)
            {
                column.StoreType = "xml";
            }
            else
            {
                var type = definition.DataType.Name.BaseIdentifier.Value;
                column.StoreType = aliases.TryGetValue(type, out var alias) ? alias : StoreType(definition.DataType);
            }

            if (definition.IdentityOptions is { } identity)
            {
                column.ValueGenerated = ValueGenerated.OnAdd;
                column["SqlServer:ValueGenerationStrategy"] = SqlServerValueGenerationStrategy.IdentityColumn;
                column["SqlServer:IdentitySeed"] = identity.IdentitySeed is null ? 1L : long.Parse(Sql(identity.IdentitySeed), CultureInfo.InvariantCulture);
                column["SqlServer:IdentityIncrement"] = identity.IdentityIncrement is null ? 1 : int.Parse(Sql(identity.IdentityIncrement), CultureInfo.InvariantCulture);
            }

            table.Columns.Add(column);
        }

        AddConstraints(table, node.Definition.TableConstraints);
    }

    private void AddConstraints(DatabaseTable table, IEnumerable<ConstraintDefinition> constraints)
    {
        foreach (var constraint in constraints)
        {
            switch (constraint)
            {
                case UniqueConstraintDefinition { IsPrimaryKey: true } primary:
                    var key = new DatabasePrimaryKey { Table = table, Name = primary.ConstraintIdentifier.Value };
                    foreach (var column in primary.Columns)
                    {
                        var property = Column(table, column.Column.MultiPartIdentifier.Identifiers[^1].Value);
                        property.IsNullable = false;
                        key.Columns.Add(property);
                    }

                    key["SqlServer:Clustered"] = primary.Clustered ?? true;
                    table.PrimaryKey = key;
                    break;
                case ForeignKeyConstraintDefinition foreign:
                    var principal = Table(foreign.ReferenceTableName);
                    var relationship = new DatabaseForeignKey
                    {
                        Table = table,
                        PrincipalTable = principal,
                        Name = foreign.ConstraintIdentifier.Value,
                        OnDelete = foreign.DeleteAction switch
                        {
                            DeleteUpdateAction.Cascade => ReferentialAction.Cascade,
                            DeleteUpdateAction.SetNull => ReferentialAction.SetNull,
                            DeleteUpdateAction.SetDefault => ReferentialAction.SetDefault,
                            _ => ReferentialAction.NoAction
                        }
                    };
                    foreach (var column in foreign.Columns)
                    {
                        relationship.Columns.Add(Column(table, column.Value));
                    }

                    foreach (var column in foreign.ReferencedTableColumns)
                    {
                        relationship.PrincipalColumns.Add(Column(principal, column.Value));
                    }

                    table.ForeignKeys.Add(relationship);
                    break;
                case CheckConstraintDefinition:
                    break; // CHECK expressions do not affect repository query/key metadata.
                default:
                    throw new InvalidOperationException($"Unsupported schema constraint: {constraint.GetType().Name}");
            }
        }
    }

    private DatabaseTable Table(SchemaObjectName name) => database.Tables.Single(table =>
        table.Schema == name.SchemaIdentifier.Value && table.Name == name.BaseIdentifier.Value);

    private static DatabaseColumn Column(DatabaseTable table, string name) => table.Columns.Single(column => column.Name == name);

    private string Sql(TSqlFragment fragment)
    {
        generator.GenerateScript(fragment, out var sql);
        return sql.Trim();
    }

    private string StoreType(DataTypeReference type) => Sql(type)
        .Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal)
        .Replace(" (", "(", StringComparison.Ordinal).Replace(", ", ",", StringComparison.Ordinal).ToLowerInvariant();

    [GeneratedRegex(@"^\s*GO\s*(?:--[^\r\n]*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BatchSeparator();

    [GeneratedRegex(@"^\s*(?:CREATE\s+(?:TABLE|TYPE)\s|ALTER\s+TABLE\s|CREATE\s+(?:UNIQUE\s+)?(?:(?:NON)?CLUSTERED\s+)?INDEX\s)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SchemaStatement();
}
