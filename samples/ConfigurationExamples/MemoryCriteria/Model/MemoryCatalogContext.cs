using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCatalog;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryCatalog;

public sealed class MemoryCatalogContext(DbContextOptions<MemoryCatalogContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<CatalogEntry>();
}
