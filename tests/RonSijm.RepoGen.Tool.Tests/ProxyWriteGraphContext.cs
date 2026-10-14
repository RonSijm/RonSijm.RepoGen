using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProxyWriteGraphContext(DbContextOptions<ProxyWriteGraphContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ProxyWriteRecord>();
        entity.Property(row => row.Label).HasDefaultValue("fixture");
        entity.Property(row => row.Version).HasComputedColumnSql("length(\"Name\")", stored: true).IsConcurrencyToken();
        var audit = modelBuilder.Entity<ProxyWriteAudit>(); audit.Property(row => row.Id).ValueGeneratedNever();
        audit.HasOne<ProxyWriteRecord>().WithMany().HasForeignKey("RecordId").OnDelete(DeleteBehavior.Cascade);
    }
}
