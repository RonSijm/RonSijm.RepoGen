using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.OrderReports;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.OrderReports;

public sealed class OrderReportsContext(DbContextOptions<OrderReportsContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Deliberately no navigation relationship: the designer joins independently mapped tables.
        modelBuilder.Entity<OrderEntry>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<DirectoryAccount>().Property(row => row.Id).ValueGeneratedNever();
    }
}
