using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class LinkWriteGeneratedContext(DbContextOptions<LinkWriteContext> options) : LinkWriteContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<LinkWriteRow>().Property<int>("JoinId").ValueGeneratedOnAdd();
        modelBuilder.Entity<LinkWriteRow>().HasKey("JoinId");
        modelBuilder.Entity<LinkWriteRow>().HasIndex(row => new { row.OwnerTenantId, row.OwnerId, row.TargetRegion, row.TargetCode }).IsUnique();
    }
}
