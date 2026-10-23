using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNativeResults;

public sealed class QueueResultsContext(DbContextOptions<QueueResultsContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TicketQueue>().Property(row => row.Id).ValueGeneratedNever();
        var ticket = modelBuilder.Entity<QueueTicket>();
        ticket.Property(row => row.Id).ValueGeneratedNever();
        ticket.Property(row => row.Revision).IsConcurrencyToken().ValueGeneratedNever();
        ticket.HasOne(row => row.Queue).WithMany(row => row.Tickets).HasForeignKey(row => row.QueueId);
    }
}
