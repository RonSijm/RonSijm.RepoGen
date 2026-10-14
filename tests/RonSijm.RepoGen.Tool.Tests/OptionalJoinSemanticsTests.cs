using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalJoinSemanticsTests
{
    [Fact]
    public async Task AnUnguardedSqliteProjectionRetainsNullTargetsAndCountsThem()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ManyReadOptionalContext(new DbContextOptionsBuilder<ManyReadContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(ManyReadTestHarness.Owners());
        context.AddRange(ManyReadTestHarness.Tags());
        context.AddRange(ManyReadTestHarness.Links());
        context.Add(new ManyReadLink { Id = 5, TenantId = 1, OwnerCode = "same", TagId = null });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var projection = context.Set<ManyReadOwner>().AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new ManyReadCard(row.Id, row.Name, row.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList()));
        projection.ToQueryString().Should().Contain("LEFT JOIN \"ManyReadTag\"");
        (await projection.ToListAsync())[0].Tags.Should().Equal(null, "Red", "Blue");
        (await context.Set<ManyReadOwner>().OrderBy(row => row.Id).Select(row => row.Tags.Count).ToListAsync()).Should().Equal(3, 1, 1, 0);
        (await context.Set<ManyReadOwner>().OrderBy(row => row.Id).Select(row => row.Tags.Where(tag => tag != null).Count()).ToListAsync()).Should().Equal(2, 1, 1, 0);
    }
}
