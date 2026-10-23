using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Sample;

[GenerateSelectors]
public sealed class Customer
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string Email { get; set; } = null!;

    public DateOnly DateOfBirth { get; set; }

    public Guid RestaurantId { get; set; }
}

[ProjectionFrom<Customer>]
public sealed record CustomerSummaryDto(Guid Id, string Name);

[ProjectionFrom<Customer>]
public sealed record CustomerReportDto(Guid Id, string Name, DateOnly DateOfBirth);

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
}

public sealed class CustomerRepository(AppDbContext context)
{
    public CustomerSelector ByEmail(string email) =>
        new(context, context.Customers.Where(customer => customer.Email == email));

    public CustomerCollectionSelector ByRestaurantId(Guid restaurantId) =>
        new(context, context.Customers.Where(customer => customer.RestaurantId == restaurantId));
}