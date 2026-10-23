using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class LinkWriteContext(DbContextOptions<LinkWriteContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LinkWriteOwner>().HasKey(row => new { row.TenantId, row.Id });
        modelBuilder.Entity<LinkWriteOwner>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<LinkWriteTarget>().HasKey(row => new { row.Region, row.Code });
        modelBuilder.Entity<LinkWriteOwner>().HasMany(row => row.Tags).WithMany(row => row.Owners).UsingEntity<LinkWriteRow>(
            target => target.HasOne(row => row.Target).WithMany(row => row.Links).HasForeignKey(row => new { row.TargetRegion, row.TargetCode }).IsRequired(),
            owner => owner.HasOne(row => row.Owner).WithMany(row => row.Links).HasForeignKey(row => new { row.OwnerTenantId, row.OwnerId }).IsRequired(),
            link =>
            {
                link.HasKey(row => new { row.OwnerTenantId, row.OwnerId, row.TargetRegion, row.TargetCode });
                link.Property(row => row.Version).IsConcurrencyToken();
                link.ToTable(table => table.HasCheckConstraint("CK_LinkNote", "\"Note\" <> 'invalid'"));
            });
    }
}
