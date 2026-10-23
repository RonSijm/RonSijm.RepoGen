using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

// Only a constant prefix plus GUID is interpolated. Own one table, never the supplied database.
#pragma warning disable EF1002
internal sealed class ReturningShadowSqlFixture(string connection, string table) : IAsyncDisposable
{
    internal ReturningShadowSqlContext Context() => new(connection, Table);
    internal static async Task<ReturningShadowSqlFixture> CreateAsync()
    {
        var fixture = new ReturningShadowSqlFixture(Environment.GetEnvironmentVariable("REPOGEN_SQLSERVER_TEST_CONNECTION")!, "RepoGenShadow_" + Guid.NewGuid().ToString("N"));
        await using var context = fixture.Context();
        await context.Database.ExecuteSqlRawAsync($"""
            IF OBJECT_ID(N'dbo.{fixture.Table}', N'U') IS NOT NULL THROW 51000, 'Fixture table collision', 1;
            CREATE TABLE [dbo].[{fixture.Table}] (
                [TenantId] int NOT NULL, [Id] int NOT NULL, [Text] nvarchar(max) NOT NULL,
                [Quantity] int NOT NULL CHECK ([Quantity] >= 0), [Revision] int NOT NULL,
                [scope]]quoted] nvarchar(32) NULL DEFAULT N'alpha', [Credit] int NOT NULL DEFAULT 7,
                [Normalized] AS UPPER([Text]), [ComputedVersion] AS ([Quantity] + 10),
                [Status] nvarchar(32) NULL, [note]]quoted] nvarchar(max) NULL,
                [shadow]]version] rowversion NOT NULL, PRIMARY KEY ([TenantId], [Id]));
            """);
        return fixture;
    }
    private string Table { get; } = table;
    public async ValueTask DisposeAsync()
    {
        await using var context = Context(); await context.Database.ExecuteSqlRawAsync($"DROP TABLE [dbo].[{Table}];");
    }
}
#pragma warning restore EF1002
