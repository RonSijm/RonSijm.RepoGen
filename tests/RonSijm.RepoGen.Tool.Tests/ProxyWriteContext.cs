using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProxyWriteContext(DbContextOptions<ProxyWriteContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ProxyWriteRecord>();
        entity.Property(row => row.Label).HasDefaultValue("fixture");
        entity.Property(row => row.Version).HasComputedColumnSql("length(\"Name\")", stored: true).IsConcurrencyToken();
        modelBuilder.Entity<ProxyWriteAudit>().Property(row => row.Id).ValueGeneratedNever();
    }
}
