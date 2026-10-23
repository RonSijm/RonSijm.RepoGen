using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryKeyIdentity;

public sealed class KeyDirectoryContext(DbContextOptions<KeyDirectoryContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entry = modelBuilder.Entity<DictionaryEntry>();
        entry.HasKey(row => row.Code); entry.Property(row => row.Code).ValueGeneratedNever();
    }
}
