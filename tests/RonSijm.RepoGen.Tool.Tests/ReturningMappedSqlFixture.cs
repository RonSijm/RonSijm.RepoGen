using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace RonSijm.RepoGen.Tool.Tests;

// The model contains only this fixture's GUID-named table; never create/delete the supplied database.
#pragma warning disable EF1002
internal sealed class ReturningMappedSqlFixture(string connection, string table) : IAsyncDisposable
{
    internal ReturningMappedContext Context() => new(new DbContextOptionsBuilder<ReturningMappedContext>().UseSqlServer(connection)
        .ReplaceService<IModelCacheKeyFactory, ReturningMappedModelCacheKeyFactory>().Options, table: table);

    internal static async Task<ReturningMappedSqlFixture> CreateAsync()
    {
        var fixture = new ReturningMappedSqlFixture(Environment.GetEnvironmentVariable("REPOGEN_SQLSERVER_TEST_CONNECTION")!, "RepoGenMapped_" + Guid.NewGuid().ToString("N"));
        await using var context = fixture.Context();
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        await using var context = Context();
        await context.Database.ExecuteSqlRawAsync("DROP TABLE " + context.GetService<ISqlGenerationHelper>().DelimitIdentifier(table));
    }
}
#pragma warning restore EF1002
