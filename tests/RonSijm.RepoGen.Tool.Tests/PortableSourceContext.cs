using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class PortableSourceContext(DbContextOptions<PortableSourceContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PortableSourceOrder>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<PortableSourceCustomer>().Property(row => row.Id).ValueGeneratedNever();
    }
}
