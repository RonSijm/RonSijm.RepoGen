using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProxyWriteCompoundContext(DbContextOptions<ProxyWriteCompoundContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<ProxyWriteCompoundRow>().HasKey(row => new { row.ParentId, row.Slot });
}
