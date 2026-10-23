using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.FixtureTickets;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.FixtureTickets;

public sealed class FixtureTicketsContext(DbContextOptions<FixtureTicketsContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FixtureTicket>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<FixtureAudit>().HasOne<FixtureTicket>().WithMany().HasForeignKey(row => row.TicketId).OnDelete(DeleteBehavior.Restrict);
    }
}
