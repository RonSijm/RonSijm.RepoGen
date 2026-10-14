using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.ProxyDirectory;

public sealed class ProxyDirectoryContext(DbContextOptions<ProxyDirectoryContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<DirectoryRecord>();
}
