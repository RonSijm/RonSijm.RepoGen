using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MappedAccessors;

public sealed class AliasDirectoryContext(DbContextOptions<AliasDirectoryContext> options) : DbContext(options)
{
    public DbSet<AliasEntry> Entries => Set<AliasEntry>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entry = modelBuilder.Entity<AliasEntry>();
        entry.Property<string>("SearchCode").HasColumnName("search code").IsRequired();
        entry.HasIndex("SearchCode").IsUnique();
        entry.Property<AliasState>("State").HasConversion<string>();
        entry.Property<bool>("Archived").HasDefaultValue(false);
        entry.HasQueryFilter(row => !EF.Property<bool>(row, "Archived"));
    }
}
