using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryProxyWrites;

public sealed class ProxyNotebookContext(DbContextOptions<ProxyNotebookContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PrivateNote>(entity =>
        {
            entity.Property(row => row.Version).HasComputedColumnSql("length(\"Title\")", stored: true).IsConcurrencyToken();
        });
        modelBuilder.Entity<PrivateAudit>().Property(row => row.Id).ValueGeneratedNever();
    }
}
