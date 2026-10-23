using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.SqlServer;
using Xunit;

#pragma warning disable EF1002 // Only a hard-coded prefix plus Guid.ToString("N") forms these owned fixture identifiers; no caller text is interpolated.

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SqlServerTemporalTests
{
    [SqlServerFixtureFact]
    public async Task TemporalRootsExecuteOnAnIsolatedVersionedTableWithUtcPeriods()
    {
        var table = "RepoGenTemporal_" + Guid.NewGuid().ToString("N");
        await using var context = new TemporalFixtureContext(Environment.GetEnvironmentVariable("REPOGEN_SQLSERVER_TEST_CONNECTION")!, table);
        // Never EnsureCreated/EnsureDeleted against an existing database. Only these two uniquely named fixture tables are owned here.
        await context.Database.ExecuteSqlRawAsync($"""
                                                   IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL THROW 51000, 'Fixture table collision', 1;
                                                   CREATE TABLE [dbo].[{table}] (
                                                       [Id] int NOT NULL PRIMARY KEY, [Name] nvarchar(100) NOT NULL,
                                                       [PeriodStart] datetime2 GENERATED ALWAYS AS ROW START NOT NULL DEFAULT SYSUTCDATETIME(),
                                                       [PeriodEnd] datetime2 GENERATED ALWAYS AS ROW END NOT NULL DEFAULT CONVERT(datetime2, '9999-12-31 23:59:59.9999999'),
                                                       PERIOD FOR SYSTEM_TIME ([PeriodStart], [PeriodEnd])
                                                   ) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[{table}_History]));
                                                   """);
        try
        {
            context.Rows.Add(new TemporalFixtureRow { Id = 1, Name = "old" });
            await context.SaveChangesAsync();
            await context.Rows.ExecuteUpdateAsync(update => update.SetProperty(row => row.Name, "new"));
            context.ChangeTracker.Clear();
            var builder = new RepositoryModelBuilder();
            builder.Configure(options => options.UseClosedRepositories("Temporal.Contracts").GenerateOnlyConfiguredEntities());
            builder.Entity<TemporalFixtureRow>().HasQuery("History")
                .FromProvider<TemporalQueryRoot<TemporalFixtureContext, TemporalFixtureRequest, TemporalFixtureRow>, TemporalFixtureRequest, TemporalFixtureRow>(request => request)
                .OrderBy(row => EF.Property<DateTime>(row, "PeriodStart"))
                .Select(row => new TemporalFixtureDto(row.Id, row.Name, EF.Property<DateTime>(row, "PeriodStart"), EF.Property<DateTime>(row, "PeriodEnd"))).List();
            var model = RepositoryModelFactory.Create(context, typeof(TemporalFixtureContext).Assembly, true, builder.Build());
            var assembly = ClosedRepositoryTests.Compile(model);
            var root = new TemporalQueryRoot<TemporalFixtureContext, TemporalFixtureRequest, TemporalFixtureRow>();
            var operations = Activator.CreateInstance(assembly.GetType(typeof(TemporalFixtureRow).Namespace + ".TemporalFixtureRowRepositoryOperations")!, context, root)!;
            var results = await (Task<IReadOnlyList<TemporalFixtureDto>>)operations.GetType().GetMethod("HistoryAsync")!.Invoke(operations, [new TemporalFixtureRequest(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)), CancellationToken.None])!;
            results.Select(static row => row.Name).Should().Equal("old", "new");
            results[0].End.Should().Be(results[1].Start);
            context.ChangeTracker.Entries().Should().BeEmpty();
            var oldTime = DateTime.SpecifyKind(results[0].Start.AddTicks((results[0].End - results[0].Start).Ticks / 2), DateTimeKind.Utc);
            var asOf = new TemporalQueryRoot<TemporalFixtureContext, TemporalFixtureRequest, TemporalFixtureRow>(TemporalQueryKind.AsOf, request => request.Start);
            (await asOf.CreateQuery(context, new(oldTime, oldTime.AddDays(1))).SingleAsync()).Name.Should().Be("old");
            foreach (var kind in new[] { TemporalQueryKind.FromTo, TemporalQueryKind.Between, TemporalQueryKind.ContainedIn })
            {
                var range = new TemporalQueryRoot<TemporalFixtureContext, TemporalFixtureRequest, TemporalFixtureRow>(kind, request => request.Start, request => request.End);
                (await range.CreateQuery(context, new(oldTime.AddDays(-1), DateTime.UtcNow.AddDays(1))).ToListAsync()).Should().HaveCount(kind == TemporalQueryKind.ContainedIn ? 1 : 2);
                range.CreateQuery(context, new(oldTime.AddDays(-1), DateTime.UtcNow.AddDays(1))).ToQueryString().Should().Contain("FOR SYSTEM_TIME");
            }

            Action localTime = () => asOf.CreateQuery(context, new(DateTime.Now, DateTime.Now));
            localTime.Should().Throw<ArgumentException>().WithMessage("*Utc*");
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync($"ALTER TABLE [dbo].[{table}] SET (SYSTEM_VERSIONING = OFF); DROP TABLE [dbo].[{table}]; DROP TABLE [dbo].[{table}_History];");
        }
    }

    [Fact]
    public void ProviderRequirementIsCheckedDuringGeneration()
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Temporal.Contracts").GenerateOnlyConfiguredEntities());
        builder.Entity<CommandRow>().HasQuery("Invalid").FromProvider<TemporalQueryRoot<CommandContext, SqlNameRequest, CommandRow>, SqlNameRequest, CommandRow>(request => request).Count();
        Action generate = () => RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*requires*SqlServer*Sqlite*");
    }
}

public sealed class SqlServerFixtureFactAttribute : FactAttribute
{
    public SqlServerFixtureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REPOGEN_SQLSERVER_TEST_CONNECTION")))
        {
            Skip = "Set REPOGEN_SQLSERVER_TEST_CONNECTION to a writable test database to run the isolated SQL Server fixtures.";
        }
    }
}

public sealed record TemporalFixtureRequest(DateTime Start, DateTime End);

public sealed record TemporalFixtureDto(int Id, string Name, DateTime Start, DateTime End);

public sealed class TemporalFixtureRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class TemporalFixtureContext(string connection, string table) : DbContext
{
    public string Table { get; } = table;
    public DbSet<TemporalFixtureRow> Rows => Set<TemporalFixtureRow>();
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseSqlServer(connection).ReplaceService<IModelCacheKeyFactory, TemporalFixtureModelKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TemporalFixtureRow>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<TemporalFixtureRow>().ToTable(Table, "dbo", tableBuilder => tableBuilder.IsTemporal(temporal =>
        {
            temporal.HasPeriodStart("PeriodStart");
            temporal.HasPeriodEnd("PeriodEnd");
            temporal.UseHistoryTable(Table + "_History", "dbo");
        }));
    }
}

public sealed class TemporalFixtureModelKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), ((TemporalFixtureContext)context).Table, designTime);
}