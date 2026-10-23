using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

#pragma warning disable EF1002 // Only a hard-coded prefix plus Guid.ToString("N") forms the owned fixture identifier.
#pragma warning disable CA1861 // Independent request arrays make input preservation and SQL parameterization explicit.

public sealed class SqlServerMembershipTests
{
    [SqlServerFixtureFact]
    public async Task LargeInputsUseParameterizedOpenJsonAndStayServerSideThroughPaging()
    {
        await WithFixture(async (context, sql) =>
        {
            var builder = MembershipQueryTests.Builder("closed");
            builder.Entity<MembershipRow>(entity =>
            {
                entity.HasVisibilityPolicy("Visible", row => row.Visible);
                entity.HasQuery("Find").WhereIn(row => row.Id, (int[] ids) => ids, options => options.Distinct().MaximumCount(10000))
                    .OrderBy(row => row.Id).Select(row => row.Id).List();
                entity.HasQuery("Page").WhereIn(row => row.Id, (int[] ids) => ids, options => options.Distinct().MaximumCount(10000))
                    .OrderBy(row => row.Id).Select(row => row.Id).Paged();
            });
            var operations = Operations(context, builder);
            var ids = Enumerable.Range(1, 6000).Concat([1, 2]).ToArray();
            var results = await (Task<IReadOnlyList<int>>)operations.GetType().GetMethod("FindAsync")!.Invoke(operations, [ids, CancellationToken.None])!;
            results.Should().Equal(1, 2, 3);
            var command = sql.Commands.Single();
            command.Text.Should().Contain("OPENJSON");
            command.Values.Should().ContainSingle();
            using var json = JsonDocument.Parse((string)command.Values.Single()!);
            json.RootElement.GetArrayLength().Should().Be(6000);
            var page = await (Task<PagedResult<int>>)operations.GetType().GetMethod("PageAsync")!.Invoke(operations, [ids, 2, 2, CancellationToken.None])!;
            page.Items.Should().Equal(3);
            page.TotalCount.Should().Be(3);
            sql.Commands.Should().HaveCount(3);
            sql.Commands.Should().OnlyContain(command => command.Text.Contains("OPENJSON", StringComparison.Ordinal));
            context.ChangeTracker.Entries().Should().BeEmpty();
            ids.Should().HaveCount(6002);
        });
    }

    [SqlServerFixtureFact]
    public async Task NullableAndNegatedInputsAreParameterizedIncludingSqlLookingText()
    {
        await WithFixture(async (context, sql) =>
        {
            var builder = MembershipQueryTests.Builder("closed");
            builder.Entity<MembershipRow>(entity =>
            {
                entity.HasVisibilityPolicy("Visible", row => row.Visible);
                entity.HasQuery("Find").WhereIn(row => row.Name, (string?[] names) => names)
                    .OrderBy(row => row.Id).Select(row => row.Id).List();
                entity.HasQuery("Exclude").WhereNotIn(row => row.Name, (string?[] names) => names)
                    .OrderBy(row => row.Id).Select(row => row.Id).List();
            });
            var operations = Operations(context, builder);
            var input = new string?[] { null, "A", "'); DROP TABLE [Anything];--" };
            var found = await (Task<IReadOnlyList<int>>)operations.GetType().GetMethod("FindAsync")!.Invoke(operations, [input, CancellationToken.None])!;
            found.Should().Equal(1, 3);
            var excluded = await (Task<IReadOnlyList<int>>)operations.GetType().GetMethod("ExcludeAsync")!.Invoke(operations, [input, CancellationToken.None])!;
            excluded.Should().Equal(2);
            var excludeA = await (Task<IReadOnlyList<int>>)operations.GetType().GetMethod("ExcludeAsync")!.Invoke(operations, [new string?[] { "A" }, CancellationToken.None])!;
            excludeA.Should().Equal(2, 3);
            sql.Commands.Should().OnlyContain(command => !command.Text.Contains("DROP TABLE", StringComparison.Ordinal));
            sql.Commands.Should().OnlyContain(command => command.Text.Contains("OPENJSON", StringComparison.Ordinal));
            (await context.Rows.CountAsync()).Should().Be(4);
        });
    }

    [SqlServerFixtureFact]
    public async Task MembershipAfterMultiSourceProjectionAndGroupingTranslatesToHaving()
    {
        await WithFixture(async (context, sql) =>
        {
            var builder = MembershipQueryTests.Builder("closed");
            builder.Entity<MembershipRow>(entity =>
            {
                entity.HasVisibilityPolicy("Visible", row => row.Visible);
                entity.HasQuery("Report").From(sources => from row in sources.Set<MembershipRow>()
                                                          join other in sources.Set<MembershipRow>() on row.Id equals other.Id
                                                          select new { row.Id, other.Category })
                    .WhereIn(row => row.Category, (MembershipReportRequest request) => request.Categories)
                    .GroupBy(row => row.Category, group => new MembershipTotal { Category = group.Key, Count = group.Count() })
                    .WhereIn(row => row.Count, (MembershipReportRequest request) => request.Counts).OrderBy(total => total.Category).List();
            });
            var operations = Operations(context, builder);
            var results = await (Task<IReadOnlyList<MembershipTotal>>)operations.GetType().GetMethod("ReportAsync")!
                .Invoke(operations, [new MembershipReportRequest(["first", "second"], [2]), CancellationToken.None])!;
            results.Select(total => (total.Category, total.Count)).Should().Equal(("first", 2));
            sql.Commands.Single().Text.Should().Contain("OPENJSON").And.Contain("INNER JOIN").And.Contain("GROUP BY").And.Contain("HAVING");
        });
    }

    private static object Operations(SqlMembershipContext context, RepositoryModelBuilder builder)
    {
        var model = RepositoryModelFactory.Create(context, typeof(MembershipRow).Assembly, true, builder.Build());
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        return Activator.CreateInstance(assembly.GetType(typeof(MembershipRow).Namespace + ".MembershipRowRepositoryOperations")!, context)!;
    }

    private static async Task WithFixture(Func<SqlMembershipContext, MembershipSql, Task> test)
    {
        var table = "RepoGenMembership_" + Guid.NewGuid().ToString("N");
        var sql = new MembershipSql();
        await using var context = new SqlMembershipContext(Environment.GetEnvironmentVariable("REPOGEN_SQLSERVER_TEST_CONNECTION")!, table, sql);
        // Never EnsureCreated/EnsureDeleted: this fixture owns only its GUID-named table in the explicitly supplied test database.
        await context.Database.ExecuteSqlRawAsync($"""
                                                   IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL THROW 51000, 'Fixture table collision', 1;
                                                   CREATE TABLE [dbo].[{table}] ([Id] int NOT NULL PRIMARY KEY, [Name] nvarchar(128) NULL, [Category] nvarchar(32) NOT NULL, [Visible] bit NOT NULL);
                                                   """);
        try
        {
            context.Rows.AddRange(new MembershipRow { Id = 1, Name = "A", Category = "first", Visible = true },
                new MembershipRow { Id = 2, Name = "B", Category = "first", Visible = true },
                new MembershipRow { Id = 3, Name = null, Category = "second", Visible = true },
                new MembershipRow { Id = 4, Name = "hidden", Category = "first", Visible = false });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            sql.Commands.Clear();
            await test(context, sql);
        }
        finally { await context.Database.ExecuteSqlRawAsync($"DROP TABLE [dbo].[{table}];"); }
    }
}

public sealed class SqlMembershipContext : DbContext
{
    internal SqlMembershipContext(string connection, string table, MembershipSql sql)
        : base(new DbContextOptionsBuilder<SqlMembershipContext>().UseSqlServer(connection, options => options.UseCompatibilityLevel(150))
            .ReplaceService<IModelCacheKeyFactory, SqlMembershipModelKey>().AddInterceptors(sql).Options) => Table = table;

    public string Table { get; }
    public DbSet<MembershipRow> Rows => Set<MembershipRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<MembershipRow>();
        row.ToTable(Table, "dbo");
        row.Property(value => value.Id).ValueGeneratedNever();
        row.Property(value => value.Name).HasMaxLength(128);
        row.Property(value => value.Category).HasMaxLength(32);
    }
}

public sealed class SqlMembershipModelKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), ((SqlMembershipContext)context).Table, designTime);
}