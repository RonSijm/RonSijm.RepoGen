using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class ManyReadContext(DbContextOptions<ManyReadContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ManyReadOwner>().HasKey(row => new { row.TenantId, row.Id });
        modelBuilder.Entity<ManyReadOwner>().HasIndex(row => row.Id).IsUnique();
        modelBuilder.Entity<ManyReadOwner>().HasAlternateKey(row => new { row.TenantId, row.Code });
        modelBuilder.Entity<ManyReadOwner>().HasMany(row => row.Tags).WithMany(row => row.Owners).UsingEntity<ManyReadLink>(
            right => right.HasOne<ManyReadTag>().WithMany().HasForeignKey(row => row.TagId).IsRequired(),
            left => left.HasOne<ManyReadOwner>().WithMany().HasForeignKey(row => new { row.TenantId, row.OwnerCode })
                .HasPrincipalKey(row => new { row.TenantId, row.Code }),
            join => { join.HasKey(row => row.Id); join.Property(row => row.Id).ValueGeneratedNever(); });
        modelBuilder.Entity<ManyReadTag>().Property(row => row.Id).ValueGeneratedNever();
    }
}
