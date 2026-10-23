using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProxyReadContext(DbContextOptions<ProxyReadContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProxyReadEntity>().Ignore(row => row.Computed);
        modelBuilder.Entity<ProxyReadEntity>().Property(row => row.PrivateCode).HasConversion(value => value.Value, value => new HiddenMemoryKey(value));
        modelBuilder.Entity<ProxyReadEntity>().HasOne(row => row.Department).WithMany().HasForeignKey(row => row.DepartmentId).IsRequired(false);
        modelBuilder.Entity<ProxyReadDepartment>();
        modelBuilder.Entity<ProxyReadPublicEntity>();
    }
}
