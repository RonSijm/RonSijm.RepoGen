using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Sample;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var restaurantId = Guid.NewGuid();
        context.Customers.Add(new Customer
        {
            Id = Guid.NewGuid(),
            Name = "Ron",
            Email = "ron@example.com",
            DateOfBirth = new DateOnly(1985, 4, 3),
            RestaurantId = restaurantId
        });
        await context.SaveChangesAsync();

        var repository = new CustomerRepository(context);
        var report = await repository
            .ByEmail("ron@example.com")
            .AsReportDtoAsync();
        var summaries = await repository
            .ByRestaurantId(restaurantId)
            .AsSummaryDtosAsync();

        Console.WriteLine($"{report?.Name}: {summaries.Count} customer(s)");
    }
}