using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.EntityFrameworkCore.Tests;

internal sealed class SqliteTestStore : IAsyncDisposable
{
    private SqliteTestStore(
        SqliteConnection connection,
        CustomerDbContext context,
        CommandCaptureInterceptor interceptor,
        Guid restaurantId)
    {
        Connection = connection;
        Context = context;
        Interceptor = interceptor;
        RestaurantId = restaurantId;
        Repository = new CustomerRepository(context);
    }

    public SqliteConnection Connection { get; }

    public CustomerDbContext Context { get; }

    public CommandCaptureInterceptor Interceptor { get; }

    public Guid RestaurantId { get; }

    public CustomerRepository Repository { get; }

    public static async Task<SqliteTestStore> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new CommandCaptureInterceptor();
        var options = new DbContextOptionsBuilder<CustomerDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        var context = new CustomerDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var restaurantId = Guid.NewGuid();
        context.Customers.AddRange(
            new Customer
            {
                Id = Guid.NewGuid(),
                Name = "Ron",
                Email = "ron@example.com",
                DateOfBirth = new DateOnly(1985, 4, 3),
                RestaurantId = restaurantId,
                Score = 7
            },
            new Customer
            {
                Id = Guid.NewGuid(),
                Name = "Ada",
                Email = "ada@example.com",
                DateOfBirth = new DateOnly(1815, 12, 10),
                RestaurantId = restaurantId,
                Score = 11
            });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        interceptor.Clear();

        return new SqliteTestStore(connection, context, interceptor, restaurantId);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await Connection.DisposeAsync();
    }
}